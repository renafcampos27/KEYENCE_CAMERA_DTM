Imports Keyence.AutoID.SDK
Imports System.Collections.Concurrent
Imports System.Text
Imports System.Threading

' Owns the Keyence ReaderAccessor on ONE dedicated background thread.
' UI and SDK callbacks may enqueue jobs; only this thread calls ReaderAccessor.
Friend NotInheritable Class KeyenceWorker

    Friend Enum OperationKind
        Connect
        Disconnect
        Command
        Diagnostics
        ReaderData
        Shutdown
    End Enum

    Friend NotInheritable Class OperationResult
        Public Kind As OperationKind
        Public Generation As Integer
        Public Purpose As String
        Public Response As String
        Public Failure As Exception
    End Class

    Private NotInheritable Class WorkItem
        Public Kind As OperationKind
        Public Generation As Integer
        Public IpAddress As String
        Public Command As String
        Public Purpose As String
        Public ReaderText As String
    End Class

    Private ReadOnly _queue As New BlockingCollection(Of WorkItem)(
        New ConcurrentQueue(Of WorkItem)()
    )
    Private ReadOnly _postToUi As Action(Of Action)
    Private ReadOnly _thread As Thread
    Private _reader As ReaderAccessor
    Private _stopping As Integer = 0

    Friend Event OperationCompleted(result As OperationResult)
    Friend Event TextReceived(generation As Integer, value As String)
    Friend Event DiagnosticCompleted(generation As Integer, lines As String())
    Friend Event WorkerFault(message As String)

    Public Sub New(postToUi As Action(Of Action))
        If postToUi Is Nothing Then Throw New ArgumentNullException("postToUi")
        _postToUi = postToUi

        _thread = New Thread(AddressOf WorkerLoop)
        _thread.Name = "Keyence-ReaderAccessor-Worker"
        _thread.IsBackground = True
        _thread.Start()
    End Sub

    Friend Function TryConnect(ipAddress As String, generation As Integer) As Boolean
        Return Enqueue(New WorkItem With {
            .Kind = OperationKind.Connect,
            .IpAddress = ipAddress,
            .Generation = generation
        })
    End Function

    Friend Function TryDisconnect(generation As Integer) As Boolean
        Return Enqueue(New WorkItem With {
            .Kind = OperationKind.Disconnect,
            .Generation = generation
        })
    End Function

    Friend Function TryCommand(
        command As String,
        purpose As String,
        generation As Integer
    ) As Boolean
        Return Enqueue(New WorkItem With {
            .Kind = OperationKind.Command,
            .Command = command,
            .Purpose = purpose,
            .Generation = generation
        })
    End Function

    Friend Function TryDiagnostics(reason As String, generation As Integer) As Boolean
        Return Enqueue(New WorkItem With {
            .Kind = OperationKind.Diagnostics,
            .Purpose = reason,
            .Generation = generation
        })
    End Function

    Private Function Enqueue(item As WorkItem) As Boolean
        If Interlocked.CompareExchange(_stopping, 0, 0) <> 0 Then Return False

        Try
            Return _queue.TryAdd(item)
        Catch ex As InvalidOperationException
            Return False
        Catch ex As ObjectDisposedException
            Return False
        End Try
    End Function

    ' Non-blocking shutdown: an SDK call stuck in native code must not freeze FormClosing.
    ' Shutdown will run after already-queued work, on the same worker thread.
    Friend Sub Shutdown()
        If Interlocked.Exchange(_stopping, 1) <> 0 Then Return

        Try
            _queue.TryAdd(New WorkItem With {.Kind = OperationKind.Shutdown})
            _queue.CompleteAdding()
        Catch
            ' The process is closing; do not wait for the SDK.
        End Try
    End Sub

    Private Sub WorkerLoop()
        Try
            For Each item As WorkItem In _queue.GetConsumingEnumerable()
                If item.Kind = OperationKind.Shutdown Then
                    Try
                        If _reader IsNot Nothing Then
                            _reader.ExecCommand("LOFF")
                        End If
                    Catch
                    End Try

                    Exit For
                End If

                If item.Kind = OperationKind.ReaderData Then
                    Dim capturedItem As WorkItem = item
                    PostToUi(Sub()
                                 EmitTextReceived(
                                     capturedItem.Generation,
                                     capturedItem.ReaderText
                                 )
                             End Sub)
                    Continue For
                End If

                If item.Kind = OperationKind.Diagnostics Then
                    ProcessDiagnostics(item)
                    Continue For
                End If

                Dim result As New OperationResult With {
                    .Kind = item.Kind,
                    .Generation = item.Generation,
                    .Purpose = item.Purpose,
                    .Response = String.Empty
                }

                Try
                    Select Case item.Kind
                        Case OperationKind.Connect
                            CloseReader()

                            Dim reader As New ReaderAccessor()
                            _reader = reader
                            reader.IpAddress = item.IpAddress

                            Dim connectionGeneration As Integer = item.Generation
                            reader.Connect(
                                Sub(data As Byte())
                                    If data Is Nothing OrElse data.Length = 0 Then Return

                                    Dim text As String = Encoding.ASCII.GetString(data)
                                    Enqueue(New WorkItem With {
                                        .Kind = OperationKind.ReaderData,
                                        .Generation = connectionGeneration,
                                        .ReaderText = text
                                    })
                                End Sub
                            )

                        Case OperationKind.Disconnect
                            CloseReader()

                        Case OperationKind.Command
                            If _reader Is Nothing Then
                                Throw New InvalidOperationException("Keyence reader is not connected.")
                            End If

                            result.Response = _reader.ExecCommand(item.Command)
                    End Select

                Catch ex As Exception
                    result.Failure = ex
                    If item.Kind = OperationKind.Connect Then CloseReader()
                End Try

                Dim completed As OperationResult = result
                PostToUi(Sub() EmitOperationCompleted(completed))
            Next
        Catch ex As Exception
            Dim failureMessage As String = ex.ToString()
            PostToUi(Sub() EmitWorkerFault(failureMessage))
        Finally
            CloseReader()

            Try
                _queue.Dispose()
            Catch
            End Try
        End Try
    End Sub

    Private Sub ProcessDiagnostics(item As WorkItem)
        Dim output As New List(Of String)()
        Dim commands() As String = {"BUSYSTAT", "ERRSTAT", "RLOCK"}

        For Each command As String In commands
            Try
                If _reader Is Nothing Then
                    Throw New InvalidOperationException("Keyence reader is not connected.")
                End If

                Dim response As String = _reader.ExecCommand(command)
                If String.IsNullOrWhiteSpace(response) Then
                    output.Add("DIAG " + command + " -> no response | " + item.Purpose)
                Else
                    Dim normalized As String = response.Trim()
                    output.Add("DIAG " + command + " -> " + normalized + " | " + item.Purpose)

                    If command = "RLOCK" AndAlso
                       normalized.Equals("OK,RLOCK,LOCK", StringComparison.OrdinalIgnoreCase) Then
                        output.Add("DIAG WARNING: reader reports RLOCK=LOCK; no UNLOCK sent.")
                    End If
                End If
            Catch ex As Exception
                output.Add("DIAG " + command + " failed: " + ex.Message)
            End Try
        Next

        Dim capturedGeneration As Integer = item.Generation
        Dim messages() As String = output.ToArray()
        PostToUi(Sub() EmitDiagnosticCompleted(capturedGeneration, messages))
    End Sub

    Private Sub CloseReader()
        Dim oldReader As ReaderAccessor = _reader
        _reader = Nothing
        If oldReader Is Nothing Then Return

        Try
            oldReader.Disconnect()
        Catch
        End Try

        Try
            oldReader.Dispose()
        Catch
        End Try
    End Sub

    Private Sub EmitOperationCompleted(result As OperationResult)
        RaiseEvent OperationCompleted(result)
    End Sub

    Private Sub EmitTextReceived(generation As Integer, value As String)
        RaiseEvent TextReceived(generation, value)
    End Sub

    Private Sub EmitDiagnosticCompleted(generation As Integer, lines As String())
        RaiseEvent DiagnosticCompleted(generation, lines)
    End Sub

    Private Sub EmitWorkerFault(message As String)
        RaiseEvent WorkerFault(message)
    End Sub

    Private Sub PostToUi(action As Action)
        Try
            _postToUi(action)
        Catch
            ' The UI may already have closed.
        End Try
    End Sub
End Class
