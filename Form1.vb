Imports Keyence.AutoID.SDK
Imports System.IO
Imports System.Text

Public Class Form1

    Private Enum CameraState
        Disconnected
        Connecting
        SynchronizingOff
        TurningOff
        WaitingOffDelay
        TurningOn
        WaitingForRead
        ProcessingRead
        Recovering
        Closing
    End Enum

    Private Enum CommandOutcome
        Success
        ExplicitFailure
        NoResponse
        UnexpectedResponse
        ExceptionThrown
    End Enum

    Private m_worker As KeyenceWorker
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
    Private PendingReadData As String = String.Empty

    ' Each TCP connection gets a new generation number. Data from an old
    ' connection is ignored after reconnecting.
    Private ReaderGeneration As Integer = 0

    Private ReadOnly LogFilePath As String =
        Path.Combine(Application.StartupPath, "KeyenceTrigger.log")

    Private Sub Form1_Load(
        sender As Object,
        e As EventArgs
    ) Handles MyBase.Load

        Try
            DataText.MaxLength = 32767

            m_worker = New KeyenceWorker(
                Sub(action As Action)
                    If ClosingApplication OrElse Me.IsDisposed OrElse
                       Not Me.IsHandleCreated Then Return

                    Try
                        Me.BeginInvoke(action)
                    Catch ex As InvalidOperationException
                        ' Form is closing.
                    End Try
                End Sub
            )
            AddHandler m_worker.OperationCompleted, AddressOf WorkerOperationCompleted
            AddHandler m_worker.TextReceived, AddressOf ReceivedDataWrite
            AddHandler m_worker.DiagnosticCompleted, AddressOf WorkerDiagnosticCompleted

            ' Identify the exact executable instance, including older builds.
            Dim appVersion As String = Me.GetType().Assembly.GetName().Version.ToString()
            Dim buildId As String =
                Me.GetType().Assembly.ManifestModule.ModuleVersionId.ToString("N")

            Me.Text = "FirstStepApp v" + appVersion +
                      " | build " + buildId.Substring(0, 8).ToUpperInvariant()

            LogStatus("APP START | version=" + appVersion +
                      " | buildId=" + buildId +
                      " | exe=" + Application.ExecutablePath)

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
            If m_worker IsNot Nothing Then m_worker.TryDisconnect(ReaderGeneration)

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
        ReaderConnected = False
        LonResyncAttempt = 0
        PendingReadData = String.Empty

        InvalidateReaderGeneration()

        If m_worker Is Nothing OrElse
           Not m_worker.TryConnect(ipAddress.Trim(), ReaderGeneration) Then
            StartRecovery("Reader connection could not be queued")
        Else
            LogStatus("Connecting reader on worker: " + ipAddress.Trim())
        End If

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

        If IsCommandResponse(upperData) OrElse IsDiagnosticResponse(upperData) Then
            LogStatus("Command/diagnostic response on data channel: " + cleanData)
            Return
        End If

        If CurrentState <> CameraState.WaitingForRead Then
            LogStatus("Reader data ignored while state is " +
                      CurrentState.ToString() + ": " + cleanData)
            Return
        End If

        CurrentState = CameraState.ProcessingRead
        NextActionAt = DateTime.MinValue
        PendingReadData = cleanData

        ' LOFF is completed on the worker BEFORE any browser SendKeys.
        If Not QueueTriggerCommand("LOFF", "AfterRead") Then
            StartRecovery("Unable to queue LOFF after reader response")
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

        If CommandInProgress Then
            LogStatus("Manual trigger request ignored: command already pending.")
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

        If ClosingApplication OrElse CommandInProgress Then Return

        Try
            Select Case CurrentState
                Case CameraState.SynchronizingOff
                    If Not IsActionDue() Then Return

                    If QueueTriggerCommand("LOFF", "SyncOff") Then
                        CurrentState = CameraState.TurningOff
                    Else
                        StartRecovery("Unable to queue synchronization LOFF")
                    End If

                Case CameraState.WaitingOffDelay
                    If Not IsActionDue() Then Return

                    If QueueTriggerCommand("LON", "StartRead") Then
                        CurrentState = CameraState.TurningOn
                    Else
                        StartRecovery("Unable to queue LON")
                    End If

                Case CameraState.Recovering
                    If Not IsActionDue() Then Return
                    AttemptReconnect()

                Case Else
                    ' No watchdog: a legitimate read can take as long as needed.
            End Select
        Catch ex As Exception
            LogStatus("State machine error: " + ex.Message)
            StartRecovery("Unhandled state machine exception")
        End Try

    End Sub

    Private Sub WorkerOperationCompleted(result As KeyenceWorker.OperationResult)

        If ClosingApplication OrElse result.Generation <> ReaderGeneration Then Return

        If result.Kind = KeyenceWorker.OperationKind.Connect Then
            If result.Failure IsNot Nothing Then
                StartRecovery("Connect failed: " + result.Failure.Message)
                Return
            End If

            ReaderConnected = True
            RecoveryAttempt = 0

            NICcomboBox.Enabled = False
            SchBtn.Enabled = False
            comboBox1.Enabled = False
            SctBtn.Enabled = True
            TgrBtn.Enabled = True

            CurrentState = CameraState.SynchronizingOff
            NextActionAt = DateTime.Now.AddMilliseconds(InitialCommandDelayMs)
            LogStatus("Reader connected on worker. Trigger synchronization scheduled.")
            Return
        End If

        If result.Kind <> KeyenceWorker.OperationKind.Command Then Return

        CommandInProgress = False
        Dim outcome As CommandOutcome = CommandOutcome.ExceptionThrown

        If result.Failure Is Nothing Then
            outcome = ClassifyCommandResponse(NormalizeReaderData(result.Response))
            LogStatus(result.Purpose + " " + result.Purpose + " " +
                      If(result.Purpose = "StartRead", "LON", "LOFF") +
                      " -> " + outcome.ToString() +
                      FormatCommandResponse(NormalizeReaderData(result.Response)))
        Else
            LogStatus(result.Purpose + " SDK exception: " + result.Failure.Message)
        End If

        Select Case result.Purpose
            Case "SyncOff"
                If CurrentState <> CameraState.TurningOff Then Return

                If outcome = CommandOutcome.ExceptionThrown OrElse
                   outcome = CommandOutcome.UnexpectedResponse Then
                    StartRecovery("LOFF exception or unexpected response during synchronization")
                    Return
                End If

                If outcome = CommandOutcome.NoResponse OrElse
                   outcome = CommandOutcome.ExplicitFailure Then
                    LogStatus("LOFF OFF state not independently confirmed.")
                End If

                ScheduleTriggerOnAfterOffDelay()

            Case "AfterRead"
                If CurrentState <> CameraState.ProcessingRead Then Return

                CompleteReaderCycle(outcome)

            Case "StartRead"
                If CurrentState <> CameraState.TurningOn Then Return

                Select Case outcome
                    Case CommandOutcome.Success
                        CurrentState = CameraState.WaitingForRead
                        NextActionAt = DateTime.MinValue
                        RecoveryAttempt = 0
                        LonResyncAttempt = 0
                        LogStatus("LON accepted by reader; waiting for a label.")

                    Case CommandOutcome.NoResponse
                        LogReaderDiagnostics("LON without response")
                        CurrentState = CameraState.WaitingForRead
                        NextActionAt = DateTime.MinValue
                        RecoveryAttempt = 0
                        LonResyncAttempt = 0
                        LogStatus("LON sent without acknowledgement; trigger state UNCONFIRMED.")

                    Case CommandOutcome.ExplicitFailure,
                         CommandOutcome.UnexpectedResponse

                        LogReaderDiagnostics("LON rejected or unexpected response")
                        LonResyncAttempt += 1

                        If LonResyncAttempt <= MaxLonResyncAttempts Then
                            CurrentState = CameraState.SynchronizingOff
                            NextActionAt = DateTime.Now.AddMilliseconds(250)
                            LogStatus("LON not confirmed; resynchronizing OFF before retry " +
                                      LonResyncAttempt.ToString() + ".")
                        Else
                            StartRecovery("LON not confirmed after resynchronization")
                        End If

                    Case CommandOutcome.ExceptionThrown
                        StartRecovery("LON communication exception")
                End Select
        End Select

    End Sub

    Private Sub CompleteReaderCycle(loffOutcome As CommandOutcome)

        Dim cleanData As String = PendingReadData
        PendingReadData = String.Empty
        Dim upperData As String = cleanData.ToUpperInvariant()

        Try
            If IsValidDtm(cleanData) Then
                LogStatus("Valid DTM received: " + cleanData)
                EnviarComoTeclado(cleanData)
            ElseIf upperData = "ERROR" OrElse upperData = "4552524F52" Then
                LogStatus("Reader returned ERROR.")
            Else
                LogStatus("Invalid reader data ignored: " + cleanData)
            End If
        Catch ex As Exception
            LogStatus("Web transfer error: " + ex.Message)
        End Try

        If loffOutcome = CommandOutcome.ExceptionThrown OrElse
           loffOutcome = CommandOutcome.UnexpectedResponse Then

            StartRecovery("LOFF exception or unexpected response after reading")
        Else
            If loffOutcome = CommandOutcome.NoResponse OrElse
               loffOutcome = CommandOutcome.ExplicitFailure Then
                LogStatus("LOFF OFF state not independently confirmed; continuing normal cycle.")
            End If

            ScheduleTriggerOnAfterOffDelay()
        End If

    End Sub

    Private Sub WorkerDiagnosticCompleted(generation As Integer, lines As String())

        If ClosingApplication OrElse generation <> ReaderGeneration Then Return

        For Each line As String In lines
            LogStatus(line)
        Next

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

    Private Function QueueTriggerCommand(command As String, purpose As String) As Boolean

        If ClosingApplication OrElse Not ReaderConnected OrElse
           CommandInProgress OrElse m_worker Is Nothing Then Return False

        If Not m_worker.TryCommand(command, purpose, ReaderGeneration) Then Return False

        CommandInProgress = True
        Return True

    End Function

    Private Function ClassifyCommandResponse(response As String) As CommandOutcome

        If String.IsNullOrWhiteSpace(response) Then
            ' Trigger command responses can be disabled in the reader.
            ' An empty response is therefore not automatically a command failure.
            Return CommandOutcome.NoResponse
        End If

        Dim normalized As String = response.Trim().ToUpperInvariant()

        If normalized = "4F4B" OrElse normalized = "OK" Then
            Return CommandOutcome.Success
        End If

        If normalized = "4552" OrElse
           normalized = "NG" OrElse normalized = "ER" Then
            Return CommandOutcome.ExplicitFailure
        End If

        ' Unexpected text is NOT a successful acknowledgement.
        Return CommandOutcome.UnexpectedResponse

    End Function

    Private Function IsCommandResponse(value As String) As Boolean

        If String.IsNullOrWhiteSpace(value) Then Return False

        Dim normalized As String = value.Trim().ToUpperInvariant()

        Return normalized = "4F4B" OrElse
               normalized = "4552" OrElse
               normalized = "NG" OrElse
               normalized = "OK" OrElse
               normalized = "ER"

    End Function

    Private Function IsDiagnosticResponse(value As String) As Boolean

        If String.IsNullOrWhiteSpace(value) Then Return False

        Dim normalized As String = value.Trim().ToUpperInvariant()

        ' These are structured diagnostic replies, not DTM read results.
        ' Do not use StartsWith("OK") or StartsWith("ER") here.
        Return normalized.StartsWith("OK,BUSYSTAT,", StringComparison.Ordinal) OrElse
               normalized.StartsWith("OK,ERRSTAT,", StringComparison.Ordinal) OrElse
               normalized.StartsWith("OK,RLOCK,", StringComparison.Ordinal) OrElse
               normalized.StartsWith("ER,BUSYSTAT,", StringComparison.Ordinal) OrElse
               normalized.StartsWith("ER,ERRSTAT,", StringComparison.Ordinal) OrElse
               normalized.StartsWith("ER,RLOCK,", StringComparison.Ordinal)

    End Function

    Private Sub LogReaderDiagnostics(reason As String)

        If ClosingApplication OrElse Not ReaderConnected OrElse
           m_worker Is Nothing Then Return

        If Not m_worker.TryDiagnostics(reason, ReaderGeneration) Then
            LogStatus("Unable to queue diagnostics: " + reason)
        End If

    End Sub

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
        PendingReadData = String.Empty

        InvalidateReaderGeneration()

        ' Queue disconnect behind any in-flight command, without blocking UI.
        If m_worker IsNot Nothing Then m_worker.TryDisconnect(ReaderGeneration)

        Dim delayMs As Integer = ReconnectDelayMs
        If RecoveryAttempt > 5 Then delayMs = LongReconnectDelayMs

        NextActionAt = DateTime.Now.AddMilliseconds(delayMs)
        TgrBtn.Enabled = False

        LogStatus("Recovery scheduled in " + delayMs.ToString() +
                  " ms. Reason: " + reason +
                  ". Attempt: " + RecoveryAttempt.ToString())

    End Sub

    Private Sub AttemptReconnect()

        If ClosingApplication Then Return

        Dim readerIp As String = DefaultKeyenceReaderIP

        If comboBox1.SelectedItem IsNot Nothing Then
            readerIp = comboBox1.SelectedItem.ToString().Trim()
        End If

        CurrentState = CameraState.Connecting
        CommandInProgress = False
        ReaderConnected = False
        InvalidateReaderGeneration()

        If m_worker Is Nothing OrElse
           Not m_worker.TryConnect(readerIp, ReaderGeneration) Then
            StartRecovery("Reconnect could not be queued")
            Return
        End If

        LogStatus("Reconnection queued on Keyence worker.")

    End Sub

    Private Sub InvalidateReaderGeneration()
        ReaderGeneration += 1
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
        InvalidateReaderGeneration()

        ' Never block the Windows Forms UI on the SDK while closing.
        If m_worker IsNot Nothing Then m_worker.Shutdown()

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
