
namespace uhr.info.detector
{
    partial class frmReport
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
            this.rtbResult = new System.Windows.Forms.RichTextBox();
            this.cmdCopyReturn = new System.Windows.Forms.Button();
            this.cmdReturn = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // rtbResult
            // 
            this.rtbResult.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbResult.BackColor = System.Drawing.Color.White;
            this.rtbResult.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.rtbResult.Location = new System.Drawing.Point(3, 2);
            this.rtbResult.Name = "rtbResult";
            this.rtbResult.ReadOnly = true;
            this.rtbResult.Size = new System.Drawing.Size(419, 159);
            this.rtbResult.TabIndex = 0;
            this.rtbResult.Text = "$ORGNAME$の調査結果は以下となります：\nUHRバージョン：\n　　フレームワークバージョン：$FRAMEVERSION$\n　　共通機能バージョン：$CORE" +
    "VERSION$$\n　　給与明細バージョン：$SALARYVERSION$\nカスタマイズファイル：$CUSTOMIZEFILECOUNT$\nマージが必要ファイル" +
    "：\n　　$MERGEFILES$";
            // 
            // cmdCopyReturn
            // 
            this.cmdCopyReturn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.cmdCopyReturn.Location = new System.Drawing.Point(12, 172);
            this.cmdCopyReturn.Name = "cmdCopyReturn";
            this.cmdCopyReturn.Size = new System.Drawing.Size(119, 23);
            this.cmdCopyReturn.TabIndex = 1;
            this.cmdCopyReturn.Text = "コピーして戻る (&C)";
            this.cmdCopyReturn.UseVisualStyleBackColor = true;
            this.cmdCopyReturn.Click += new System.EventHandler(this.cmdCopyReturn_Click);
            // 
            // cmdReturn
            // 
            this.cmdReturn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cmdReturn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cmdReturn.Location = new System.Drawing.Point(317, 172);
            this.cmdReturn.Name = "cmdReturn";
            this.cmdReturn.Size = new System.Drawing.Size(95, 23);
            this.cmdReturn.TabIndex = 2;
            this.cmdReturn.Text = "戻る (&X)";
            this.cmdReturn.UseVisualStyleBackColor = true;
            this.cmdReturn.Click += new System.EventHandler(this.cmdReturn_Click);
            // 
            // frmReport
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cmdReturn;
            this.ClientSize = new System.Drawing.Size(416, 204);
            this.ControlBox = false;
            this.Controls.Add(this.cmdReturn);
            this.Controls.Add(this.cmdCopyReturn);
            this.Controls.Add(this.rtbResult);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Name = "frmReport";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "調査結果";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.RichTextBox rtbResult;
        private System.Windows.Forms.Button cmdCopyReturn;
        private System.Windows.Forms.Button cmdReturn;
    }
}