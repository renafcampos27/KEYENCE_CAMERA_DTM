Imports Keyence.AutoID.SDK
Imports System.IO
Imports System.Text

Public Class Form1

    Private Enum CameraState
        Disconnected
        Connecting
        SynchronizingOff
        WaitingOffDelay
        WaitingForRead
        ProcessingRead
        Recovering
        Closing
    End Enum

    Private Enum CommandOutcome
        Success
        ExplicitFailure
        NoResponse
        ExceptionThrown
    End Enum

    Private m_reader As ReaderAccessor = New ReaderAccessor()
    Private m_searcher As ReaderSearcher = New ReaderSearcher()
    Private m_nicList As List(Of NicSearchResult) = New List(Of NicSearchResult)()

    Private LastValue As String = "Nothing"

    Private Const DefaultNetworkCardIP As String = "192.168.100.99"
    Private Const DefaultKeyenceReaderIP As String = "192.168.100.100"

    Private Const MinimumDtmLength As Integer = 27
    Private Const MainTimerIntervalMs As Integer = 50
    Private Const InitialCommandDelayMs As Integer = 250
    Private Const TriggerOffDelayMs As Integer = 500
    Private Const ReconnectDelayMs As Integer = 1000
    Private Const LongReconnectDelayMs As Integer = 5000
    Private Const MaxLonResyncAttempts As Integer = 2

    Private CurrentState As CameraState = CameraState.Disconnected
    Private NextActionAt As DateTime = DateTime.MinValue
    Private ReaderConnected As Boolean = False
    Private CommandInProgress As Boolean = False
    Private ClosingApplication As Boolean = False
    Private RecoveryAttempt As Integer = 0
    Private LonResyncAttempt As Integer = 0

    ' Each TCP connection gets a new generation number. Data from an old
    ' connection is ignored after reconnecting.
    Private ReaderGeneration As Integer = 0

    Private ReadOnly LogFilePath As String =
        Path.Combine(Application.StartupPath, "KeyenceTrigger.log")

    Private Delegate Sub ReaderDataDelegate(value As String, generation As Integer)

    Private Sub Form1_Load(
        sender As Object,
        e As EventArgs
    ) Handles MyBase.Load

        Try
            DataText.MaxLength = 32767

            Timer1.Stop()
            Timer1.Interval = MainTimerIntervalMs

            LoadNetworkCards()
            Timer1.Start()

            If NICcomboBox.SelectedItem IsNot Nothing AndAlso
               NICcomboBox.SelectedItem.ToString() = DefaultNetworkCardIP Then

                SchBtn.PerformClick()
            Else
                LogStatus("Default network card not found: " + DefaultNetworkCardIP)
            End If

        Catch ex As Exception
            LogStatus("Application startup error: " + ex.Message)
        End Try

    End Sub

    Private Sub LoadNetworkCards()

        NICcomboBox.Items.Clear()
        m_nicList = m_searcher.ListUpNic()

        If m_nicList Is Nothing OrElse m_nicList.Count = 0 Then
            Throw New InvalidOperationException("No network cards were found.")
        End If

        Dim defaultIndex As Integer = -1

        For i As Integer = 0 To m_nicList.Count - 1
            NICcomboBox.Items.Add(m_nicList(i).NicIpAddr)

            If m_nicList(i).NicIpAddr = DefaultNetworkCardIP Then
                defaultIndex = i
            End If
        Next

        If defaultIndex >= 0 Then
            NICcomboBox.SelectedIndex = defaultIndex
        Else
            NICcomboBox.SelectedIndex = 0
        End If

    End Sub

    Private Sub SchBtn_Click(
        sender As Object,
        e As EventArgs
    ) Handles SchBtn.Click

        Try
            If ClosingApplication Then Return
            If m_searcher.IsSearching Then Return

            If m_nicList Is Nothing OrElse
               NICcomboBox.SelectedIndex < 0 OrElse
               NICcomboBox.SelectedIndex >= m_nicList.Count Then

                LogStatus("No valid network card selected.")
                Return
            End If

            m_searcher.SelectedNicSearchResult =
                m_nicList(NICcomboBox.SelectedIndex)

            NICcomboBox.Enabled = False
            SchBtn.Enabled = False
            SctBtn.Enabled = False
            comboBox1.Items.Clear()

            LogStatus("Searching for Keyence reader...")
            m_searcher.Start(AddressOf ReaderDetectedAction)

        Catch ex As Exception
            LogStatus("Reader search error: " + ex.Message)
            NICcomboBox.Enabled = True
            SchBtn.Enabled = True
        End Try

    End Sub

    Private Sub ReaderDetectedAction(result As ReaderSearchResult)

        If ClosingApplication Then Return
        If result Is Nothing Then Return
        If Not IsHandleCreated OrElse IsDisposed Then Return

        Try
            BeginInvoke(
                New Action(Of String)(AddressOf SearchListUp),
                result.IpAddress
            )
        Catch
            ' The form can be closing while the search callback is running.
        End Try

    End Sub

    Private Sub SearchListUp(ipAddress As String)

        If ClosingApplication Then Return
        If String.IsNullOrWhiteSpace(ipAddress) Then Return

        Dim cleanIp As String = ipAddress.Trim()

        If Not comboBox1.Items.Contains(cleanIp) Then
            comboBox1.Items.Add(cleanIp)
        End If

        If cleanIp = DefaultKeyenceReaderIP Then
            comboBox1.SelectedItem = cleanIp
            SctBtn.Enabled = True

            Try
                If m_searcher.IsSearching Then m_searcher.Stop()
            Catch
                ' Search stop is not critical after the target reader was found.
            End Try

            If Not SctBtn.Checked Then
                SctBtn.Checked = True
            End If
        End If

    End Sub

    Private Sub SctBtn_CheckedChanged(
        sender As Object,
        e As EventArgs
    ) Handles SctBtn.CheckedChanged

        If ClosingApplication Then Return

        If SctBtn.Checked Then
            If comboBox1.SelectedItem Is Nothing Then
                SctBtn.Checked = False
                Return
            End If

            ConnectReader(comboBox1.SelectedItem.ToString())
        Else
            CurrentState = CameraState.Disconnected
            ReaderConnected = False
            NextActionAt = DateTime.MinValue

            InvalidateReaderGeneration()
            SafeDisconnectAndDisposeReader()

            NICcomboBox.Enabled = True
            SchBtn.Enabled = True
            comboBox1.Enabled = True
            TgrBtn.Enabled = False

            LogStatus("Reader disconnected by user.")
        End If

    End Sub

    Private Sub ConnectReader(ipAddress As String)

        If ClosingApplication Then Return

        CurrentState = CameraState.Connecting
        CommandInProgress = False
        LonResyncAttempt = 0

        Try
            InvalidateReaderGeneration()
            SafeDisconnectAndDisposeReader()

            m_reader = New ReaderAccessor()
            m_reader.IpAddress = ipAddress.Trim()

            ReaderGeneration += 1
            Dim thisGeneration As Integer = ReaderGeneration

            ' Keep the production connection simple. LiveView is not started.
            m_reader.Connect(
                Sub(data As Byte()) ReceivedDataAction(data, thisGeneration)
            )

            ReaderConnected = True
            RecoveryAttempt = 0

            NICcomboBox.Enabled = False
            SchBtn.Enabled = False
            comboBox1.Enabled = False
            SctBtn.Enabled = True
            TgrBtn.Enabled = True

            ' IMPORTANT:
            ' LOFF is used only to create a known OFF edge before LON.
            ' An ER response to LOFF is NOT fatal because Keyence documents
            ' that LOFF can return failure when the reader is already in
            ' reading standby (already OFF).
            CurrentState = CameraState.SynchronizingOff
            NextActionAt = DateTime.Now.AddMilliseconds(InitialCommandDelayMs)

            LogStatus("Reader connected. Trigger synchronization scheduled.")

        Catch ex As Exception
            ReaderConnected = False
            LogStatus("Reader connection error: " + ex.Message)
            StartRecovery("Connection failed")
        End Try

    End Sub

    Private Sub ReceivedDataAction(data As Byte(), generation As Integer)

        If ClosingApplication Then Return
        If generation <> ReaderGeneration Then Return
        If data Is Nothing OrElse data.Length = 0 Then Return
        If Not IsHandleCreated OrElse IsDisposed Then Return

        Dim receivedText As String = Encoding.ASCII.GetString(data)

        Try
            BeginInvoke(
                New ReaderDataDelegate(AddressOf ReceivedDataWrite),
                receivedText,
                generation
            )
        Catch
            ' The form can be closing while data is arriving.
        End Try

    End Sub

    Private Sub ReceivedDataWrite(receivedData As String, generation As Integer)

        If ClosingApplication Then Return
        If generation <> ReaderGeneration Then
            SafeWriteLog("Stale callback ignored from an old reader session.")
            Return
        End If

        Dim cleanData As String = NormalizeReaderData(receivedData)
        If String.IsNullOrWhiteSpace(cleanData) Then Return

        Dim upperData As String = cleanData.ToUpperInvariant()

        ' Command responses belong to ExecCommand. If the SDK also forwards
        ' them through the data callback, they must never be handled as labels.
        If IsCommandResponse(upperData) Then
            LogStatus("Command response on data channel: " + cleanData)
            Return
        End If

        If CurrentState <> CameraState.WaitingForRead Then
            LogStatus(
                "Reader data ignored while state is " +
                CurrentState.ToString() + ": " + cleanData
            )
            Return
        End If

        CurrentState = CameraState.ProcessingRead
        NextActionAt = DateTime.MinValue

        ' Close the trigger before touching the browser. This prevents browser
        ' activity from overlapping with LON/LOFF commands.
        Dim loffOutcome As CommandOutcome =
            ExecuteTriggerCommand("LOFF", "Close cycle after reader response")

        Try
            If IsValidDtm(cleanData) Then
                LogStatus("Valid DTM received: " + cleanData)
                EnviarComoTeclado(cleanData)

            ElseIf upperData = "ERROR" OrElse
                   upperData = "4552524F52" Then

                LogStatus("Reader returned ERROR.")

            Else
                LogStatus("Invalid reader data ignored: " + cleanData)
            End If

        Catch ex As Exception
            LogStatus("Web transfer error: " + ex.Message)
        End Try

        If loffOutcome = CommandOutcome.ExceptionThrown Then
            StartRecovery("LOFF communication exception after reader response")
        Else
            ' Success, ER, empty or unknown response all mean that the command
            ' transaction completed. ER is allowed here because the reader can
            ' already be in standby after finishing the read.
            ScheduleTriggerOnAfterOffDelay()
        End If

    End Sub

    Private Sub TgrBtn_Click(
        sender As Object,
        e As EventArgs
    ) Handles TgrBtn.Click

        If ClosingApplication Then Return

        If Not ReaderConnected Then
            StartRecovery("Manual reconnect request")
            Return
        End If

        ' Manual trigger request is always a complete resynchronization.
        ' We never send a blind second LON.
        LonResyncAttempt = 0
        CurrentState = CameraState.SynchronizingOff
        NextActionAt = DateTime.Now
        LogStatus("Manual trigger resynchronization requested.")

    End Sub

    Private Sub Timer1_Tick(
        sender As Object,
        e As EventArgs
    ) Handles Timer1.Tick

        If ClosingApplication Then Return
        If CommandInProgress Then Return

        Try
            Select Case CurrentState

                Case CameraState.SynchronizingOff
                    If Not IsActionDue() Then Return

                    Dim loffOutcome As CommandOutcome =
                        ExecuteTriggerCommand("LOFF", "Synchronize trigger OFF")

                    If loffOutcome = CommandOutcome.ExceptionThrown Then
                        StartRecovery("LOFF communication exception during synchronization")
                    Else
                        ' Do NOT block here on ER or an empty response.
                        ' ER can mean that LOFF was sent while already OFF.
                        ScheduleTriggerOnAfterOffDelay()
                    End If

                Case CameraState.WaitingOffDelay
                    If Not IsActionDue() Then Return

                    Dim lonOutcome As CommandOutcome =
                        ExecuteTriggerCommand("LON", "Start read cycle")

                    Select Case lonOutcome
                        Case CommandOutcome.Success,
                             CommandOutcome.NoResponse

                            CurrentState = CameraState.WaitingForRead
                            NextActionAt = DateTime.MinValue
                            RecoveryAttempt = 0
                            LonResyncAttempt = 0

                            LogStatus(
                                "LON command completed. Waiting for a label."
                            )

                        Case CommandOutcome.ExplicitFailure
                            LonResyncAttempt += 1

                            If LonResyncAttempt <= MaxLonResyncAttempts Then
                                CurrentState = CameraState.SynchronizingOff
                                NextActionAt = DateTime.Now.AddMilliseconds(250)

                                LogStatus(
                                    "LON returned ER/NG. Resynchronizing OFF before retry " +
                                    LonResyncAttempt.ToString() + "."
                                )
                            Else
                                StartRecovery("LON rejected after resynchronization")
                            End If

                        Case CommandOutcome.ExceptionThrown
                            StartRecovery("LON communication exception")
                    End Select

                Case CameraState.Recovering
                    If Not IsActionDue() Then Return
                    AttemptReconnect()

                Case Else
                    ' No periodic LON/LOFF is sent while waiting for a label.
            End Select

        Catch ex As Exception
            LogStatus("State machine error: " + ex.Message)
            StartRecovery("Unhandled state machine exception")
        End Try

    End Sub

    Private Sub ScheduleTriggerOnAfterOffDelay()

        CurrentState = CameraState.WaitingOffDelay
        NextActionAt = DateTime.Now.AddMilliseconds(TriggerOffDelayMs)

        LogStatus(
            "OFF interval started. Next LON in " +
            TriggerOffDelayMs.ToString() + " ms."
        )

    End Sub

    Private Function IsActionDue() As Boolean
        Return NextActionAt = DateTime.MinValue OrElse DateTime.Now >= NextActionAt
    End Function

    Private Function ExecuteTriggerCommand(
        command As String,
        reason As String
    ) As CommandOutcome

        If ClosingApplication AndAlso command.ToUpperInvariant() <> "LOFF" Then
            Return CommandOutcome.ExceptionThrown
        End If

        If Not ReaderConnected OrElse m_reader Is Nothing Then
            LogStatus(command + " not sent because reader is not connected.")
            Return CommandOutcome.ExceptionThrown
        End If

        If CommandInProgress Then
            LogStatus(command + " not sent because another command is active.")
            Return CommandOutcome.ExceptionThrown
        End If

        CommandInProgress = True

        Try
            ' Use the same SDK call form that worked in the original Keyence
            ' sample/application. Do not make trigger operation depend on an
            ' exact textual response.
            Dim response As String =
                NormalizeReaderData(m_reader.ExecCommand(command))

            Dim outcome As CommandOutcome = ClassifyCommandResponse(response)

            LogStatus(
                command + " -> " + outcome.ToString() +
                FormatCommandResponse(response) +
                " | " + reason
            )

            Return outcome

        Catch ex As Exception
            LogStatus(
                command + " exception: " + ex.Message +
                " | " + reason
            )

            Return CommandOutcome.ExceptionThrown

        Finally
            CommandInProgress = False
        End Try

    End Function

    Private Function ClassifyCommandResponse(response As String) As CommandOutcome

        If String.IsNullOrWhiteSpace(response) Then
            ' Trigger command responses can be disabled in the reader.
            ' An empty response is therefore not automatically a command failure.
            Return CommandOutcome.NoResponse
        End If

        Dim normalized As String = response.Trim().ToUpperInvariant()

        If normalized = "4F4B" OrElse
           normalized.StartsWith("OK") Then
            Return CommandOutcome.Success
        End If

        If normalized = "4552" OrElse
           normalized = "NG" OrElse
           normalized.StartsWith("ER") Then
            Return CommandOutcome.ExplicitFailure
        End If

        ' Unknown text is treated as no explicit failure. The command was sent
        ' and the SDK returned normally, so do not create a reconnect storm.
        Return CommandOutcome.NoResponse

    End Function

    Private Function IsCommandResponse(value As String) As Boolean

        If String.IsNullOrWhiteSpace(value) Then Return False

        Dim normalized As String = value.Trim().ToUpperInvariant()

        Return normalized = "4F4B" OrElse
               normalized = "4552" OrElse
               normalized = "NG" OrElse
               normalized.StartsWith("OK") OrElse
               normalized.StartsWith("ER")

    End Function

    Private Function FormatCommandResponse(response As String) As String

        If String.IsNullOrWhiteSpace(response) Then
            Return " (empty response)"
        End If

        Return " (response: " + response + ")"

    End Function

    Private Sub StartRecovery(reason As String)

        If ClosingApplication Then Return

        RecoveryAttempt += 1
        CurrentState = CameraState.Recovering
        ReaderConnected = False
        CommandInProgress = False
        LonResyncAttempt = 0

        InvalidateReaderGeneration()
        SafeDisconnectAndDisposeReader()

        Dim delayMs As Integer = ReconnectDelayMs
        If RecoveryAttempt > 5 Then delayMs = LongReconnectDelayMs

        NextActionAt = DateTime.Now.AddMilliseconds(delayMs)
        TgrBtn.Enabled = False

        LogStatus(
            "Recovery scheduled in " + delayMs.ToString() +
            " ms. Reason: " + reason +
            ". Attempt: " + RecoveryAttempt.ToString()
        )

    End Sub

    Private Sub AttemptReconnect()

        If ClosingApplication Then Return

        Dim readerIp As String = DefaultKeyenceReaderIP

        If comboBox1.SelectedItem IsNot Nothing Then
            readerIp = comboBox1.SelectedItem.ToString().Trim()
        End If

        CurrentState = CameraState.Connecting

        Try
            m_reader = New ReaderAccessor()
            m_reader.IpAddress = readerIp

            ReaderGeneration += 1
            Dim thisGeneration As Integer = ReaderGeneration

            m_reader.Connect(
                Sub(data As Byte()) ReceivedDataAction(data, thisGeneration)
            )

            ReaderConnected = True
            TgrBtn.Enabled = True
            LonResyncAttempt = 0

            CurrentState = CameraState.SynchronizingOff
            NextActionAt = DateTime.Now.AddMilliseconds(InitialCommandDelayMs)

            LogStatus("Reader reconnected. Trigger synchronization scheduled.")

        Catch ex As Exception
            RecoveryAttempt += 1
            ReaderConnected = False

            InvalidateReaderGeneration()
            SafeDisconnectAndDisposeReader()

            CurrentState = CameraState.Recovering

            Dim delayMs As Integer = ReconnectDelayMs
            If RecoveryAttempt > 5 Then delayMs = LongReconnectDelayMs

            NextActionAt = DateTime.Now.AddMilliseconds(delayMs)

            LogStatus(
                "Reconnect failed: " + ex.Message +
                ". Next attempt in " + delayMs.ToString() + " ms."
            )
        End Try

    End Sub

    Private Sub InvalidateReaderGeneration()
        ReaderGeneration += 1
    End Sub

    Private Sub SafeDisconnectAndDisposeReader()

        If m_reader Is Nothing Then Return

        Try
            m_reader.Disconnect()
        Catch
            ' Ignore disconnect errors during recovery.
        End Try

        Try
            m_reader.Dispose()
        Catch
            ' Ignore dispose errors during recovery.
        End Try

    End Sub

    Private Function NormalizeReaderData(value As String) As String

        If value Is Nothing Then Return String.Empty

        Dim normalized As String = value.Replace(vbCr, String.Empty)
        normalized = normalized.Replace(vbLf, String.Empty)
        normalized = normalized.Replace(ChrW(0), String.Empty)

        Return normalized.Trim()

    End Function

    Private Function IsValidDtm(value As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(value) AndAlso value.Length >= MinimumDtmLength
    End Function

    Public Sub EnviarComoTeclado(valorKeyence As String)

        If String.IsNullOrWhiteSpace(valorKeyence) Then Return

        Dim cleanValue As String = valorKeyence.Trim()
        If cleanValue.Length < MinimumDtmLength Then Return

        If CheckBox1.Checked AndAlso cleanValue = LastValue Then
            LogStatus("Duplicate DTM blocked: " + cleanValue)
            Return
        End If

        If CheckBox2.Checked Then
            SendKeys.SendWait("^a")
            SendKeys.SendWait("{DEL}")
        End If

        SendKeys.SendWait(cleanValue)
        SendKeys.SendWait("{ENTER}")

        LastValue = cleanValue
        LogStatus("DTM sent to the web field: " + cleanValue)

    End Sub

    Private Sub Form1_FormClosing(
        sender As Object,
        e As FormClosingEventArgs
    ) Handles Me.FormClosing

        ClosingApplication = True
        CurrentState = CameraState.Closing
        Timer1.Stop()

        Try
            If ReaderConnected AndAlso Not CommandInProgress Then
                CommandInProgress = True

                Try
                    ' Best effort only. Closing must never wait on state logic.
                    m_reader.ExecCommand("LOFF")
                Catch
                Finally
                    CommandInProgress = False
                End Try
            End If
        Catch
        End Try

        InvalidateReaderGeneration()
        SafeDisconnectAndDisposeReader()

        Try
            m_searcher.Stop()
        Catch
        End Try

        Try
            m_searcher.Dispose()
        Catch
        End Try

        Try
            liveviewForm1.EndReceive()
        Catch
        End Try

        Try
            liveviewForm1.Dispose()
        Catch
        End Try

    End Sub

    Private Sub LogStatus(message As String)

        Dim line As String =
            "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + message

        Try
            DataText.Text = line
        Catch
        End Try

        SafeWriteLog(line)

    End Sub

    Private Sub SafeWriteLog(message As String)

        Try
            File.AppendAllText(
                LogFilePath,
                message + Environment.NewLine,
                Encoding.UTF8
            )
        Catch
            ' Logging must never stop trigger control.
        End Try

    End Sub

End Class
