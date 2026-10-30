<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    'フォームがコンポーネントの一覧をクリーンアップするために dispose をオーバーライドします。
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Windows フォーム デザイナーで必要です。
    Private components As System.ComponentModel.IContainer

    'メモ: 以下のプロシージャは Windows フォーム デザイナーで必要です。
    'Windows フォーム デザイナーを使用して変更できます。  
    'コード エディターを使って変更しないでください。
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()
        Me.SchBtn = New System.Windows.Forms.Button()
        Me.comboBox1 = New System.Windows.Forms.ComboBox()
        Me.DataText = New System.Windows.Forms.TextBox()
        Me.TgrBtn = New System.Windows.Forms.Button()
        Me.liveviewForm1 = New Keyence.AutoID.SDK.LiveviewForm()
        Me.SctBtn = New System.Windows.Forms.CheckBox()
        Me.NICcomboBox = New System.Windows.Forms.ComboBox()
        Me.Timer1 = New System.Windows.Forms.Timer(Me.components)
        Me.CheckBox1 = New System.Windows.Forms.CheckBox()
        Me.CheckBox2 = New System.Windows.Forms.CheckBox()
        Me.SuspendLayout()
        '
        'SchBtn
        '
        Me.SchBtn.Location = New System.Drawing.Point(166, 9)
        Me.SchBtn.Name = "SchBtn"
        Me.SchBtn.Size = New System.Drawing.Size(83, 25)
        Me.SchBtn.TabIndex = 13
        Me.SchBtn.Text = "Search"
        Me.SchBtn.UseVisualStyleBackColor = True
        '
        'comboBox1
        '
        Me.comboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.comboBox1.FormattingEnabled = True
        Me.comboBox1.Location = New System.Drawing.Point(10, 40)
        Me.comboBox1.Name = "comboBox1"
        Me.comboBox1.Size = New System.Drawing.Size(150, 21)
        Me.comboBox1.TabIndex = 12
        '
        'DataText
        '
        Me.DataText.BackColor = System.Drawing.SystemColors.Control
        Me.DataText.Location = New System.Drawing.Point(10, 636)
        Me.DataText.MaxLength = 10
        Me.DataText.Name = "DataText"
        Me.DataText.ScrollBars = System.Windows.Forms.ScrollBars.Both
        Me.DataText.Size = New System.Drawing.Size(686, 20)
        Me.DataText.TabIndex = 11
        '
        'TgrBtn
        '
        Me.TgrBtn.Enabled = False
        Me.TgrBtn.Location = New System.Drawing.Point(10, 67)
        Me.TgrBtn.Name = "TgrBtn"
        Me.TgrBtn.Size = New System.Drawing.Size(103, 28)
        Me.TgrBtn.TabIndex = 10
        Me.TgrBtn.Text = "Trigger On"
        Me.TgrBtn.UseVisualStyleBackColor = True
        '
        'liveviewForm1
        '
        Me.liveviewForm1.BackColor = System.Drawing.Color.Black
        Me.liveviewForm1.BinningType = Keyence.AutoID.SDK.LiveviewForm.ImageBinningType.OneQuarter
        Me.liveviewForm1.ImageFormat = Keyence.AutoID.SDK.LiveviewForm.ImageFormatType.Jpeg
        Me.liveviewForm1.ImageQuality = 5
        Me.liveviewForm1.IpAddress = "192.168.100.100"
        Me.liveviewForm1.Location = New System.Drawing.Point(10, 113)
        Me.liveviewForm1.Name = "liveviewForm1"
        Me.liveviewForm1.PullTimeSpan = 100
        Me.liveviewForm1.Size = New System.Drawing.Size(686, 517)
        Me.liveviewForm1.TabIndex = 8
        Me.liveviewForm1.TimeoutMs = 2000
        '
        'SctBtn
        '
        Me.SctBtn.Appearance = System.Windows.Forms.Appearance.Button
        Me.SctBtn.AutoSize = True
        Me.SctBtn.Enabled = False
        Me.SctBtn.Location = New System.Drawing.Point(166, 40)
        Me.SctBtn.Name = "SctBtn"
        Me.SctBtn.Size = New System.Drawing.Size(82, 23)
        Me.SctBtn.TabIndex = 14
        Me.SctBtn.Text = "ReaderSelect"
        Me.SctBtn.UseVisualStyleBackColor = True
        '
        'NICcomboBox
        '
        Me.NICcomboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.NICcomboBox.FormattingEnabled = True
        Me.NICcomboBox.Location = New System.Drawing.Point(13, 9)
        Me.NICcomboBox.Name = "NICcomboBox"
        Me.NICcomboBox.Size = New System.Drawing.Size(147, 21)
        Me.NICcomboBox.TabIndex = 15
        '
        'Timer1
        '
        Me.Timer1.Interval = 500
        '
        'CheckBox1
        '
        Me.CheckBox1.AutoSize = True
        Me.CheckBox1.Checked = True
        Me.CheckBox1.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBox1.Location = New System.Drawing.Point(167, 67)
        Me.CheckBox1.Name = "CheckBox1"
        Me.CheckBox1.Size = New System.Drawing.Size(212, 17)
        Me.CheckBox1.TabIndex = 16
        Me.CheckBox1.Text = "Block Transfer if DTM=Last DTM value"
        Me.CheckBox1.UseVisualStyleBackColor = True
        '
        'CheckBox2
        '
        Me.CheckBox2.AutoSize = True
        Me.CheckBox2.Checked = True
        Me.CheckBox2.CheckState = System.Windows.Forms.CheckState.Checked
        Me.CheckBox2.Location = New System.Drawing.Point(166, 90)
        Me.CheckBox2.Name = "CheckBox2"
        Me.CheckBox2.Size = New System.Drawing.Size(249, 17)
        Me.CheckBox2.TabIndex = 17
        Me.CheckBox2.Text = "Clear TextBox text before write new DTM value"
        Me.CheckBox2.UseVisualStyleBackColor = True
        '
        'Form1
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(708, 697)
        Me.Controls.Add(Me.CheckBox2)
        Me.Controls.Add(Me.CheckBox1)
        Me.Controls.Add(Me.NICcomboBox)
        Me.Controls.Add(Me.SctBtn)
        Me.Controls.Add(Me.SchBtn)
        Me.Controls.Add(Me.comboBox1)
        Me.Controls.Add(Me.DataText)
        Me.Controls.Add(Me.TgrBtn)
        Me.Controls.Add(Me.liveviewForm1)
        Me.Name = "Form1"
        Me.Text = "FirstStepApp"
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Private WithEvents SchBtn As Button
    Private WithEvents comboBox1 As ComboBox
    Private WithEvents DataText As TextBox
    Private WithEvents TgrBtn As Button
    Private WithEvents liveviewForm1 As Keyence.AutoID.SDK.LiveviewForm
    Private WithEvents SctBtn As CheckBox
    Friend WithEvents NICcomboBox As ComboBox
    Friend WithEvents Timer1 As Timer
    Friend WithEvents CheckBox1 As CheckBox
    Friend WithEvents CheckBox2 As CheckBox
End Class
