namespace HTPCAVRVolume
{
    partial class HTPCAVRVolume
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.btnVolUp = new System.Windows.Forms.Button();
            this.btnVolDown = new System.Windows.Forms.Button();
            this.btnToggleMute = new System.Windows.Forms.Button();
            this.cmbDevice = new System.Windows.Forms.ComboBox();
            this.lblDevice = new System.Windows.Forms.Label();
            this.lblIP = new System.Windows.Forms.Label();
            this.tbIP = new System.Windows.Forms.TextBox();
            this.btnSave = new System.Windows.Forms.Button();
            this.notifyIcon = new System.Windows.Forms.NotifyIcon(this.components);
            this.lblAudioStatus = new System.Windows.Forms.Label();
            this.chkStartWithWindows = new System.Windows.Forms.CheckBox();
            this.chkKeepAlive = new System.Windows.Forms.CheckBox();
            this.lblTVIP = new System.Windows.Forms.Label();
            this.tbTVIP = new System.Windows.Forms.TextBox();
            this.lblTVStatus = new System.Windows.Forms.Label();
            this.btnAVROn = new System.Windows.Forms.Button();
            this.btnAVROff = new System.Windows.Forms.Button();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.SuspendLayout();
            // 
            // btnVolUp
            // 
            this.btnVolUp.Location = new System.Drawing.Point(112, 65);
            this.btnVolUp.Name = "btnVolUp";
            this.btnVolUp.Size = new System.Drawing.Size(81, 25);
            this.btnVolUp.TabIndex = 0;
            this.btnVolUp.Text = "VolUp";
            this.btnVolUp.UseVisualStyleBackColor = true;
            this.btnVolUp.Click += new System.EventHandler(this.BtnVolUp_Click);
            // 
            // btnVolDown
            // 
            this.btnVolDown.Location = new System.Drawing.Point(12, 65);
            this.btnVolDown.Name = "btnVolDown";
            this.btnVolDown.Size = new System.Drawing.Size(94, 25);
            this.btnVolDown.TabIndex = 1;
            this.btnVolDown.Text = "VolDown";
            this.btnVolDown.UseVisualStyleBackColor = true;
            this.btnVolDown.Click += new System.EventHandler(this.BtnVolDown_Click);
            // 
            // btnToggleMute
            // 
            this.btnToggleMute.Location = new System.Drawing.Point(199, 65);
            this.btnToggleMute.Name = "btnToggleMute";
            this.btnToggleMute.Size = new System.Drawing.Size(88, 25);
            this.btnToggleMute.TabIndex = 3;
            this.btnToggleMute.Text = "Mute/UnMute";
            this.btnToggleMute.UseVisualStyleBackColor = true;
            this.btnToggleMute.Click += new System.EventHandler(this.BtnToggleMute_Click);
            // 
            // cmbDevice
            // 
            this.cmbDevice.FormattingEnabled = true;
            this.cmbDevice.Items.AddRange(new object[] {
            "DenonMarantz",
            "StormAudio"});
            this.cmbDevice.Location = new System.Drawing.Point(62, 12);
            this.cmbDevice.Name = "cmbDevice";
            this.cmbDevice.Size = new System.Drawing.Size(131, 21);
            this.cmbDevice.TabIndex = 4;
            // 
            // lblDevice
            // 
            this.lblDevice.AutoSize = true;
            this.lblDevice.Location = new System.Drawing.Point(12, 15);
            this.lblDevice.Name = "lblDevice";
            this.lblDevice.Size = new System.Drawing.Size(44, 13);
            this.lblDevice.TabIndex = 5;
            this.lblDevice.Text = "Device:";
            // 
            // lblIP
            // 
            this.lblIP.AutoSize = true;
            this.lblIP.Location = new System.Drawing.Point(12, 42);
            this.lblIP.Name = "lblIP";
            this.lblIP.Size = new System.Drawing.Size(20, 13);
            this.lblIP.TabIndex = 6;
            this.lblIP.Text = "IP:";
            // 
            // tbIP
            // 
            this.tbIP.Location = new System.Drawing.Point(62, 39);
            this.tbIP.Name = "tbIP";
            this.tbIP.Size = new System.Drawing.Size(131, 20);
            this.tbIP.TabIndex = 7;
            // 
            // btnSave
            // 
            this.btnSave.Location = new System.Drawing.Point(199, 12);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(88, 47);
            this.btnSave.TabIndex = 8;
            this.btnSave.Text = "Save";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.BtnSave_Click);
            // 
            // notifyIcon
            // 
            this.notifyIcon.Text = "HTPC-AVR-sync";
            this.notifyIcon.Visible = true;
            this.notifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.NotifyIcon_MouseDoubleClick);
            //
            // lblAudioStatus
            //
            this.lblAudioStatus.AutoSize = false;
            this.lblAudioStatus.Location = new System.Drawing.Point(12, 96);
            this.lblAudioStatus.Name = "lblAudioStatus";
            this.lblAudioStatus.Size = new System.Drawing.Size(270, 13);
            this.lblAudioStatus.TabIndex = 9;
            this.lblAudioStatus.Text = "Audio: Not configured";
            //
            // HTPCAVRVolume
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            //
            // chkStartWithWindows
            //
            this.chkStartWithWindows.AutoSize = true;
            this.chkStartWithWindows.Location = new System.Drawing.Point(12, 116);
            this.chkStartWithWindows.Name = "chkStartWithWindows";
            this.chkStartWithWindows.Size = new System.Drawing.Size(120, 17);
            this.chkStartWithWindows.TabIndex = 10;
            this.chkStartWithWindows.Text = "Start with Windows";
            this.chkStartWithWindows.UseVisualStyleBackColor = true;
            this.chkStartWithWindows.CheckedChanged += new System.EventHandler(this.ChkStartWithWindows_CheckedChanged);
            //
            // chkKeepAlive
            //
            this.chkKeepAlive.AutoSize = true;
            this.chkKeepAlive.Location = new System.Drawing.Point(12, 136);
            this.chkKeepAlive.Name = "chkKeepAlive";
            this.chkKeepAlive.Size = new System.Drawing.Size(120, 17);
            this.chkKeepAlive.TabIndex = 17;
            this.chkKeepAlive.Text = "Keep audio device alive";
            this.chkKeepAlive.UseVisualStyleBackColor = true;
            this.chkKeepAlive.CheckedChanged += new System.EventHandler(this.ChkKeepAlive_CheckedChanged);
            //
            // lblTVIP
            //
            this.lblTVIP.AutoSize = true;
            this.lblTVIP.Location = new System.Drawing.Point(12, 165);
            this.lblTVIP.Name = "lblTVIP";
            this.lblTVIP.Size = new System.Drawing.Size(35, 13);
            this.lblTVIP.TabIndex = 11;
            this.lblTVIP.Text = "TV IP:";
            //
            // tbTVIP
            //
            this.tbTVIP.Location = new System.Drawing.Point(62, 162);
            this.tbTVIP.Name = "tbTVIP";
            this.tbTVIP.Size = new System.Drawing.Size(131, 20);
            this.tbTVIP.TabIndex = 12;
            //
            // lblTVStatus
            //
            this.lblTVStatus.AutoSize = false;
            this.lblTVStatus.Location = new System.Drawing.Point(12, 189);
            this.lblTVStatus.Name = "lblTVStatus";
            this.lblTVStatus.Size = new System.Drawing.Size(270, 13);
            this.lblTVStatus.TabIndex = 13;
            this.lblTVStatus.Text = "TV: Not configured";
            //
            // btnAVROn
            //
            this.btnAVROn.Location = new System.Drawing.Point(12, 208);
            this.btnAVROn.Name = "btnAVROn";
            this.btnAVROn.Size = new System.Drawing.Size(80, 25);
            this.btnAVROn.TabIndex = 14;
            this.btnAVROn.Text = "AVR On";
            this.btnAVROn.UseVisualStyleBackColor = true;
            this.btnAVROn.Click += new System.EventHandler(this.BtnAVROn_Click);
            //
            // btnAVROff
            //
            this.btnAVROff.Location = new System.Drawing.Point(100, 208);
            this.btnAVROff.Name = "btnAVROff";
            this.btnAVROff.Size = new System.Drawing.Size(80, 25);
            this.btnAVROff.TabIndex = 15;
            this.btnAVROff.Text = "AVR Off";
            this.btnAVROff.UseVisualStyleBackColor = true;
            this.btnAVROff.Click += new System.EventHandler(this.BtnAVROff_Click);
            //
            // rtbLog
            //
            this.rtbLog.Location = new System.Drawing.Point(12, 240);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.rtbLog.Size = new System.Drawing.Size(270, 65);
            this.rtbLog.TabIndex = 16;
            this.rtbLog.Text = "";
            this.rtbLog.BackColor = System.Drawing.SystemColors.Control;
            this.rtbLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.rtbLog.Font = new System.Drawing.Font("Consolas", 7.5F);
            //
            // HTPCAVRVolume
            //
            this.ClientSize = new System.Drawing.Size(294, 315);
            this.Controls.Add(this.rtbLog);
            this.Controls.Add(this.btnAVROff);
            this.Controls.Add(this.btnAVROn);
            this.Controls.Add(this.lblTVStatus);
            this.Controls.Add(this.tbTVIP);
            this.Controls.Add(this.lblTVIP);
            this.Controls.Add(this.chkKeepAlive);
            this.Controls.Add(this.chkStartWithWindows);
            this.Controls.Add(this.lblAudioStatus);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.tbIP);
            this.Controls.Add(this.lblIP);
            this.Controls.Add(this.lblDevice);
            this.Controls.Add(this.cmbDevice);
            this.Controls.Add(this.btnToggleMute);
            this.Controls.Add(this.btnVolDown);
            this.Controls.Add(this.btnVolUp);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "HTPCAVRVolume";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "HTPCAVRVolume";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.HTPCAVRVolume_FormClosed);
            this.Load += new System.EventHandler(this.HTPCAVRVolume_Load);
            this.Shown += new System.EventHandler(this.HTPCAVRVolume_Shown);
            this.Resize += new System.EventHandler(this.HTPCAVRVolume_Resize);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnVolUp;
        private System.Windows.Forms.Button btnVolDown;
        private System.Windows.Forms.Button btnToggleMute;
        private System.Windows.Forms.ComboBox cmbDevice;
        private System.Windows.Forms.Label lblDevice;
        private System.Windows.Forms.Label lblIP;
        private System.Windows.Forms.TextBox tbIP;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.NotifyIcon notifyIcon;
        private System.Windows.Forms.Label lblAudioStatus;
        private System.Windows.Forms.CheckBox chkStartWithWindows;
        private System.Windows.Forms.CheckBox chkKeepAlive;
        private System.Windows.Forms.Label lblTVIP;
        private System.Windows.Forms.TextBox tbTVIP;
        private System.Windows.Forms.Label lblTVStatus;
        private System.Windows.Forms.Button btnAVROn;
        private System.Windows.Forms.Button btnAVROff;
        private System.Windows.Forms.RichTextBox rtbLog;
    }
}

