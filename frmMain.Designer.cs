namespace uhr.info.detector
{
    partial class frmMain
    {
        /// <summary>
        /// 必要なデザイナー変数です。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 使用中のリソースをすべてクリーンアップします。
        /// </summary>
        /// <param name="disposing">マネージド リソースを破棄する場合は true を指定し、その他の場合は false を指定します。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows フォーム デザイナーで生成されたコード

        /// <summary>
        /// デザイナー サポートに必要なメソッドです。このメソッドの内容を
        /// コード エディターで変更しないでください。
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmMain));
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.tslStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.txtOrgFilter = new System.Windows.Forms.TextBox();
            this.lstOrgs = new System.Windows.Forms.ListBox();
            this.grpBaseInfo = new System.Windows.Forms.GroupBox();
            this.txtYearAdjustVersion = new System.Windows.Forms.TextBox();
            this.lblYearAdjustVersion = new System.Windows.Forms.Label();
            this.lblFWCustomized = new System.Windows.Forms.Label();
            this.txtSalaryVersion = new System.Windows.Forms.TextBox();
            this.lblSalaryVersion = new System.Windows.Forms.Label();
            this.txtCoreVersion = new System.Windows.Forms.TextBox();
            this.lblCoreVersion = new System.Windows.Forms.Label();
            this.txtFWVersion = new System.Windows.Forms.TextBox();
            this.lblFWVersion = new System.Windows.Forms.Label();
            this.txtOrgName = new System.Windows.Forms.TextBox();
            this.txtOrgCode = new System.Windows.Forms.TextBox();
            this.lblOrgName = new System.Windows.Forms.Label();
            this.lblOrgCode = new System.Windows.Forms.Label();
            this.grpCustomizedInfo = new System.Windows.Forms.GroupBox();
            this.cmdMergeFilePrepare = new System.Windows.Forms.Button();
            this.lblCustomizedFileCount = new System.Windows.Forms.Label();
            this.cmdFilePrepare = new System.Windows.Forms.Button();
            this.lstMergeNeedsFile = new System.Windows.Forms.ListBox();
            this.lblMergeNeedsFiles = new System.Windows.Forms.Label();
            this.lstCustomizedFile = new System.Windows.Forms.ListBox();
            this.lblCustomizedFiles = new System.Windows.Forms.Label();
            this.grpUpgradeInfo = new System.Windows.Forms.GroupBox();
            this.lblYearAdjustTargetVersion = new System.Windows.Forms.Label();
            this.cboYearAdjustTargetVersion = new System.Windows.Forms.ComboBox();
            this.cboSalaryTargetVersion = new System.Windows.Forms.ComboBox();
            this.cboCoreTargetVersion = new System.Windows.Forms.ComboBox();
            this.cboFWTargetVersion = new System.Windows.Forms.ComboBox();
            this.lblSalaryTargetVersion = new System.Windows.Forms.Label();
            this.lblCoreTargetVersion = new System.Windows.Forms.Label();
            this.lblFWTargetVersion = new System.Windows.Forms.Label();
            this.cmdShowReport = new System.Windows.Forms.Button();
            this.cmdSearch = new System.Windows.Forms.Button();
            this.stfDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.fldBrowser = new System.Windows.Forms.FolderBrowserDialog();
            this.statusStrip1.SuspendLayout();
            this.grpBaseInfo.SuspendLayout();
            this.grpCustomizedInfo.SuspendLayout();
            this.grpUpgradeInfo.SuspendLayout();
            this.SuspendLayout();
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tslStatus});
            this.statusStrip1.Location = new System.Drawing.Point(0, 615);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(1100, 22);
            this.statusStrip1.TabIndex = 0;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // tslStatus
            // 
            this.tslStatus.Name = "tslStatus";
            this.tslStatus.Size = new System.Drawing.Size(1085, 17);
            this.tslStatus.Spring = true;
            this.tslStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtOrgFilter
            // 
            this.txtOrgFilter.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtOrgFilter.Location = new System.Drawing.Point(8, 9);
            this.txtOrgFilter.Name = "txtOrgFilter";
            this.txtOrgFilter.Size = new System.Drawing.Size(193, 19);
            this.txtOrgFilter.TabIndex = 1;
            this.txtOrgFilter.TextChanged += new System.EventHandler(this.txtOrgFilter_TextChanged);
            // 
            // lstOrgs
            // 
            this.lstOrgs.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.lstOrgs.Font = new System.Drawing.Font("ＭＳ Ｐゴシック", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lstOrgs.FormattingEnabled = true;
            this.lstOrgs.ItemHeight = 12;
            this.lstOrgs.Location = new System.Drawing.Point(8, 34);
            this.lstOrgs.Name = "lstOrgs";
            this.lstOrgs.Size = new System.Drawing.Size(255, 556);
            this.lstOrgs.TabIndex = 2;
            this.lstOrgs.DoubleClick += new System.EventHandler(this.lstOrgs_DoubleClick);
            this.lstOrgs.KeyDown += new System.Windows.Forms.KeyEventHandler(this.lstOrgs_KeyDown);
            // 
            // grpBaseInfo
            // 
            this.grpBaseInfo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpBaseInfo.Controls.Add(this.txtYearAdjustVersion);
            this.grpBaseInfo.Controls.Add(this.lblYearAdjustVersion);
            this.grpBaseInfo.Controls.Add(this.lblFWCustomized);
            this.grpBaseInfo.Controls.Add(this.txtSalaryVersion);
            this.grpBaseInfo.Controls.Add(this.lblSalaryVersion);
            this.grpBaseInfo.Controls.Add(this.txtCoreVersion);
            this.grpBaseInfo.Controls.Add(this.lblCoreVersion);
            this.grpBaseInfo.Controls.Add(this.txtFWVersion);
            this.grpBaseInfo.Controls.Add(this.lblFWVersion);
            this.grpBaseInfo.Controls.Add(this.txtOrgName);
            this.grpBaseInfo.Controls.Add(this.txtOrgCode);
            this.grpBaseInfo.Controls.Add(this.lblOrgName);
            this.grpBaseInfo.Controls.Add(this.lblOrgCode);
            this.grpBaseInfo.Location = new System.Drawing.Point(269, 9);
            this.grpBaseInfo.Name = "grpBaseInfo";
            this.grpBaseInfo.Size = new System.Drawing.Size(449, 198);
            this.grpBaseInfo.TabIndex = 3;
            this.grpBaseInfo.TabStop = false;
            this.grpBaseInfo.Text = "基本情報・現在バージョン";
            // 
            // txtYearAdjustVersion
            // 
            this.txtYearAdjustVersion.Location = new System.Drawing.Point(127, 165);
            this.txtYearAdjustVersion.Name = "txtYearAdjustVersion";
            this.txtYearAdjustVersion.Size = new System.Drawing.Size(316, 19);
            this.txtYearAdjustVersion.TabIndex = 12;
            this.txtYearAdjustVersion.WordWrap = false;
            // 
            // lblYearAdjustVersion
            // 
            this.lblYearAdjustVersion.AutoSize = true;
            this.lblYearAdjustVersion.Location = new System.Drawing.Point(20, 168);
            this.lblYearAdjustVersion.Name = "lblYearAdjustVersion";
            this.lblYearAdjustVersion.Size = new System.Drawing.Size(98, 12);
            this.lblYearAdjustVersion.TabIndex = 11;
            this.lblYearAdjustVersion.Text = "年末調整バージョン";
            // 
            // lblFWCustomized
            // 
            this.lblFWCustomized.AutoSize = true;
            this.lblFWCustomized.Location = new System.Drawing.Point(218, 86);
            this.lblFWCustomized.Name = "lblFWCustomized";
            this.lblFWCustomized.Size = new System.Drawing.Size(0, 12);
            this.lblFWCustomized.TabIndex = 10;
            // 
            // txtSalaryVersion
            // 
            this.txtSalaryVersion.Location = new System.Drawing.Point(127, 133);
            this.txtSalaryVersion.Name = "txtSalaryVersion";
            this.txtSalaryVersion.Size = new System.Drawing.Size(85, 19);
            this.txtSalaryVersion.TabIndex = 9;
            this.txtSalaryVersion.WordWrap = false;
            // 
            // lblSalaryVersion
            // 
            this.lblSalaryVersion.AutoSize = true;
            this.lblSalaryVersion.Location = new System.Drawing.Point(20, 136);
            this.lblSalaryVersion.Name = "lblSalaryVersion";
            this.lblSalaryVersion.Size = new System.Drawing.Size(98, 12);
            this.lblSalaryVersion.TabIndex = 8;
            this.lblSalaryVersion.Text = "給与明細バージョン";
            // 
            // txtCoreVersion
            // 
            this.txtCoreVersion.Location = new System.Drawing.Point(127, 106);
            this.txtCoreVersion.Name = "txtCoreVersion";
            this.txtCoreVersion.Size = new System.Drawing.Size(85, 19);
            this.txtCoreVersion.TabIndex = 7;
            this.txtCoreVersion.WordWrap = false;
            // 
            // lblCoreVersion
            // 
            this.lblCoreVersion.AutoSize = true;
            this.lblCoreVersion.Location = new System.Drawing.Point(20, 109);
            this.lblCoreVersion.Name = "lblCoreVersion";
            this.lblCoreVersion.Size = new System.Drawing.Size(98, 12);
            this.lblCoreVersion.TabIndex = 6;
            this.lblCoreVersion.Text = "共通機能バージョン";
            // 
            // txtFWVersion
            // 
            this.txtFWVersion.Location = new System.Drawing.Point(127, 79);
            this.txtFWVersion.Name = "txtFWVersion";
            this.txtFWVersion.Size = new System.Drawing.Size(85, 19);
            this.txtFWVersion.TabIndex = 5;
            this.txtFWVersion.WordWrap = false;
            // 
            // lblFWVersion
            // 
            this.lblFWVersion.AutoSize = true;
            this.lblFWVersion.Location = new System.Drawing.Point(4, 82);
            this.lblFWVersion.Name = "lblFWVersion";
            this.lblFWVersion.Size = new System.Drawing.Size(114, 12);
            this.lblFWVersion.TabIndex = 4;
            this.lblFWVersion.Text = "フレームワークバージョン";
            // 
            // txtOrgName
            // 
            this.txtOrgName.Location = new System.Drawing.Point(127, 52);
            this.txtOrgName.Name = "txtOrgName";
            this.txtOrgName.Size = new System.Drawing.Size(202, 19);
            this.txtOrgName.TabIndex = 3;
            this.txtOrgName.WordWrap = false;
            // 
            // txtOrgCode
            // 
            this.txtOrgCode.Location = new System.Drawing.Point(127, 25);
            this.txtOrgCode.Name = "txtOrgCode";
            this.txtOrgCode.Size = new System.Drawing.Size(85, 19);
            this.txtOrgCode.TabIndex = 2;
            this.txtOrgCode.WordWrap = false;
            // 
            // lblOrgName
            // 
            this.lblOrgName.AutoSize = true;
            this.lblOrgName.Location = new System.Drawing.Point(77, 55);
            this.lblOrgName.Name = "lblOrgName";
            this.lblOrgName.Size = new System.Drawing.Size(41, 12);
            this.lblOrgName.TabIndex = 1;
            this.lblOrgName.Text = "機構名";
            // 
            // lblOrgCode
            // 
            this.lblOrgCode.AutoSize = true;
            this.lblOrgCode.Location = new System.Drawing.Point(62, 28);
            this.lblOrgCode.Name = "lblOrgCode";
            this.lblOrgCode.Size = new System.Drawing.Size(56, 12);
            this.lblOrgCode.TabIndex = 0;
            this.lblOrgCode.Text = "機構コード";
            // 
            // grpCustomizedInfo
            // 
            this.grpCustomizedInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpCustomizedInfo.Controls.Add(this.cmdMergeFilePrepare);
            this.grpCustomizedInfo.Controls.Add(this.lblCustomizedFileCount);
            this.grpCustomizedInfo.Controls.Add(this.cmdFilePrepare);
            this.grpCustomizedInfo.Controls.Add(this.lstMergeNeedsFile);
            this.grpCustomizedInfo.Controls.Add(this.lblMergeNeedsFiles);
            this.grpCustomizedInfo.Controls.Add(this.lstCustomizedFile);
            this.grpCustomizedInfo.Controls.Add(this.lblCustomizedFiles);
            this.grpCustomizedInfo.Location = new System.Drawing.Point(269, 213);
            this.grpCustomizedInfo.Name = "grpCustomizedInfo";
            this.grpCustomizedInfo.Size = new System.Drawing.Size(819, 358);
            this.grpCustomizedInfo.TabIndex = 4;
            this.grpCustomizedInfo.TabStop = false;
            this.grpCustomizedInfo.Text = "カスタマイズ情報";
            // 
            // cmdMergeFilePrepare
            // 
            this.cmdMergeFilePrepare.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.cmdMergeFilePrepare.Enabled = false;
            this.cmdMergeFilePrepare.Location = new System.Drawing.Point(701, 189);
            this.cmdMergeFilePrepare.Name = "cmdMergeFilePrepare";
            this.cmdMergeFilePrepare.Size = new System.Drawing.Size(112, 32);
            this.cmdMergeFilePrepare.TabIndex = 9;
            this.cmdMergeFilePrepare.Text = "マージファイル準備";
            this.cmdMergeFilePrepare.UseVisualStyleBackColor = true;
            this.cmdMergeFilePrepare.Click += new System.EventHandler(this.cmdMergeFilePrepare_Click);
            // 
            // lblCustomizedFileCount
            // 
            this.lblCustomizedFileCount.AutoSize = true;
            this.lblCustomizedFileCount.Location = new System.Drawing.Point(125, 25);
            this.lblCustomizedFileCount.Name = "lblCustomizedFileCount";
            this.lblCustomizedFileCount.Size = new System.Drawing.Size(0, 12);
            this.lblCustomizedFileCount.TabIndex = 7;
            // 
            // cmdFilePrepare
            // 
            this.cmdFilePrepare.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.cmdFilePrepare.Enabled = false;
            this.cmdFilePrepare.Location = new System.Drawing.Point(701, 40);
            this.cmdFilePrepare.Name = "cmdFilePrepare";
            this.cmdFilePrepare.Size = new System.Drawing.Size(112, 32);
            this.cmdFilePrepare.TabIndex = 8;
            this.cmdFilePrepare.Text = "全ファイル準備";
            this.cmdFilePrepare.UseVisualStyleBackColor = true;
            this.cmdFilePrepare.Click += new System.EventHandler(this.cmdFilePrepare_Click);
            // 
            // lstMergeNeedsFile
            // 
            this.lstMergeNeedsFile.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lstMergeNeedsFile.FormattingEnabled = true;
            this.lstMergeNeedsFile.ItemHeight = 12;
            this.lstMergeNeedsFile.Location = new System.Drawing.Point(15, 189);
            this.lstMergeNeedsFile.Name = "lstMergeNeedsFile";
            this.lstMergeNeedsFile.Size = new System.Drawing.Size(680, 148);
            this.lstMergeNeedsFile.TabIndex = 4;
            // 
            // lblMergeNeedsFiles
            // 
            this.lblMergeNeedsFiles.AutoSize = true;
            this.lblMergeNeedsFiles.Location = new System.Drawing.Point(13, 173);
            this.lblMergeNeedsFiles.Name = "lblMergeNeedsFiles";
            this.lblMergeNeedsFiles.Size = new System.Drawing.Size(212, 12);
            this.lblMergeNeedsFiles.TabIndex = 3;
            this.lblMergeNeedsFiles.Text = "バージョンマージ必要ファイル（マージファイル）";
            // 
            // lstCustomizedFile
            // 
            this.lstCustomizedFile.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lstCustomizedFile.FormattingEnabled = true;
            this.lstCustomizedFile.ItemHeight = 12;
            this.lstCustomizedFile.Location = new System.Drawing.Point(15, 40);
            this.lstCustomizedFile.Name = "lstCustomizedFile";
            this.lstCustomizedFile.Size = new System.Drawing.Size(680, 124);
            this.lstCustomizedFile.TabIndex = 2;
            // 
            // lblCustomizedFiles
            // 
            this.lblCustomizedFiles.AutoSize = true;
            this.lblCustomizedFiles.Location = new System.Drawing.Point(13, 25);
            this.lblCustomizedFiles.Name = "lblCustomizedFiles";
            this.lblCustomizedFiles.Size = new System.Drawing.Size(163, 12);
            this.lblCustomizedFiles.TabIndex = 1;
            this.lblCustomizedFiles.Text = "カスタマイズ済ファイル（全ファイル）";
            // 
            // grpUpgradeInfo
            // 
            this.grpUpgradeInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.grpUpgradeInfo.Controls.Add(this.lblYearAdjustTargetVersion);
            this.grpUpgradeInfo.Controls.Add(this.cboYearAdjustTargetVersion);
            this.grpUpgradeInfo.Controls.Add(this.cboSalaryTargetVersion);
            this.grpUpgradeInfo.Controls.Add(this.cboCoreTargetVersion);
            this.grpUpgradeInfo.Controls.Add(this.cboFWTargetVersion);
            this.grpUpgradeInfo.Controls.Add(this.lblSalaryTargetVersion);
            this.grpUpgradeInfo.Controls.Add(this.lblCoreTargetVersion);
            this.grpUpgradeInfo.Controls.Add(this.lblFWTargetVersion);
            this.grpUpgradeInfo.Location = new System.Drawing.Point(724, 9);
            this.grpUpgradeInfo.Name = "grpUpgradeInfo";
            this.grpUpgradeInfo.Size = new System.Drawing.Size(364, 198);
            this.grpUpgradeInfo.TabIndex = 5;
            this.grpUpgradeInfo.TabStop = false;
            this.grpUpgradeInfo.Text = "目標バージョン情報";
            // 
            // lblYearAdjustTargetVersion
            // 
            this.lblYearAdjustTargetVersion.AutoSize = true;
            this.lblYearAdjustTargetVersion.Location = new System.Drawing.Point(6, 109);
            this.lblYearAdjustTargetVersion.Name = "lblYearAdjustTargetVersion";
            this.lblYearAdjustTargetVersion.Size = new System.Drawing.Size(53, 12);
            this.lblYearAdjustTargetVersion.TabIndex = 19;
            this.lblYearAdjustTargetVersion.Text = "年末調整";
            // 
            // cboYearAdjustTargetVersion
            // 
            this.cboYearAdjustTargetVersion.FormattingEnabled = true;
            this.cboYearAdjustTargetVersion.Location = new System.Drawing.Point(81, 105);
            this.cboYearAdjustTargetVersion.Name = "cboYearAdjustTargetVersion";
            this.cboYearAdjustTargetVersion.Size = new System.Drawing.Size(116, 20);
            this.cboYearAdjustTargetVersion.TabIndex = 18;
            // 
            // cboSalaryTargetVersion
            // 
            this.cboSalaryTargetVersion.FormattingEnabled = true;
            this.cboSalaryTargetVersion.Location = new System.Drawing.Point(81, 77);
            this.cboSalaryTargetVersion.Name = "cboSalaryTargetVersion";
            this.cboSalaryTargetVersion.Size = new System.Drawing.Size(116, 20);
            this.cboSalaryTargetVersion.TabIndex = 17;
            // 
            // cboCoreTargetVersion
            // 
            this.cboCoreTargetVersion.FormattingEnabled = true;
            this.cboCoreTargetVersion.Location = new System.Drawing.Point(81, 49);
            this.cboCoreTargetVersion.Name = "cboCoreTargetVersion";
            this.cboCoreTargetVersion.Size = new System.Drawing.Size(116, 20);
            this.cboCoreTargetVersion.TabIndex = 16;
            // 
            // cboFWTargetVersion
            // 
            this.cboFWTargetVersion.FormattingEnabled = true;
            this.cboFWTargetVersion.Location = new System.Drawing.Point(81, 21);
            this.cboFWTargetVersion.Name = "cboFWTargetVersion";
            this.cboFWTargetVersion.Size = new System.Drawing.Size(116, 20);
            this.cboFWTargetVersion.TabIndex = 15;
            // 
            // lblSalaryTargetVersion
            // 
            this.lblSalaryTargetVersion.AutoSize = true;
            this.lblSalaryTargetVersion.Location = new System.Drawing.Point(6, 81);
            this.lblSalaryTargetVersion.Name = "lblSalaryTargetVersion";
            this.lblSalaryTargetVersion.Size = new System.Drawing.Size(53, 12);
            this.lblSalaryTargetVersion.TabIndex = 14;
            this.lblSalaryTargetVersion.Text = "給与明細";
            // 
            // lblCoreTargetVersion
            // 
            this.lblCoreTargetVersion.AutoSize = true;
            this.lblCoreTargetVersion.Location = new System.Drawing.Point(6, 53);
            this.lblCoreTargetVersion.Name = "lblCoreTargetVersion";
            this.lblCoreTargetVersion.Size = new System.Drawing.Size(53, 12);
            this.lblCoreTargetVersion.TabIndex = 12;
            this.lblCoreTargetVersion.Text = "共通機能";
            // 
            // lblFWTargetVersion
            // 
            this.lblFWTargetVersion.AutoSize = true;
            this.lblFWTargetVersion.Location = new System.Drawing.Point(6, 25);
            this.lblFWTargetVersion.Name = "lblFWTargetVersion";
            this.lblFWTargetVersion.Size = new System.Drawing.Size(69, 12);
            this.lblFWTargetVersion.TabIndex = 10;
            this.lblFWTargetVersion.Text = "フレームワーク";
            // 
            // cmdShowReport
            // 
            this.cmdShowReport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cmdShowReport.Location = new System.Drawing.Point(980, 580);
            this.cmdShowReport.Name = "cmdShowReport";
            this.cmdShowReport.Size = new System.Drawing.Size(102, 32);
            this.cmdShowReport.TabIndex = 6;
            this.cmdShowReport.Text = "調査結果報告";
            this.cmdShowReport.UseVisualStyleBackColor = true;
            this.cmdShowReport.Click += new System.EventHandler(this.cmdShowReport_Click);
            // 
            // cmdSearch
            // 
            this.cmdSearch.Location = new System.Drawing.Point(202, 8);
            this.cmdSearch.Name = "cmdSearch";
            this.cmdSearch.Size = new System.Drawing.Size(64, 22);
            this.cmdSearch.TabIndex = 7;
            this.cmdSearch.Text = "調査 (&C)";
            this.cmdSearch.UseVisualStyleBackColor = true;
            this.cmdSearch.Click += new System.EventHandler(this.cmdSearch_Click);
            // 
            // frmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1100, 637);
            this.Controls.Add(this.cmdSearch);
            this.Controls.Add(this.cmdShowReport);
            this.Controls.Add(this.grpUpgradeInfo);
            this.Controls.Add(this.grpCustomizedInfo);
            this.Controls.Add(this.grpBaseInfo);
            this.Controls.Add(this.lstOrgs);
            this.Controls.Add(this.txtOrgFilter);
            this.Controls.Add(this.statusStrip1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(800, 561);
            this.Name = "frmMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "UHRアップグレード情報確認";
            this.Load += new System.EventHandler(this.frmMain_Load);
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.grpBaseInfo.ResumeLayout(false);
            this.grpBaseInfo.PerformLayout();
            this.grpCustomizedInfo.ResumeLayout(false);
            this.grpCustomizedInfo.PerformLayout();
            this.grpUpgradeInfo.ResumeLayout(false);
            this.grpUpgradeInfo.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.TextBox txtOrgFilter;
        private System.Windows.Forms.ListBox lstOrgs;
        private System.Windows.Forms.GroupBox grpBaseInfo;
        private System.Windows.Forms.TextBox txtSalaryVersion;
        private System.Windows.Forms.Label lblSalaryVersion;
        private System.Windows.Forms.TextBox txtCoreVersion;
        private System.Windows.Forms.Label lblCoreVersion;
        private System.Windows.Forms.TextBox txtFWVersion;
        private System.Windows.Forms.Label lblFWVersion;
        private System.Windows.Forms.TextBox txtOrgName;
        private System.Windows.Forms.TextBox txtOrgCode;
        private System.Windows.Forms.Label lblOrgName;
        private System.Windows.Forms.Label lblOrgCode;
        private System.Windows.Forms.GroupBox grpCustomizedInfo;
        private System.Windows.Forms.GroupBox grpUpgradeInfo;
        private System.Windows.Forms.ComboBox cboSalaryTargetVersion;
        private System.Windows.Forms.ComboBox cboCoreTargetVersion;
        private System.Windows.Forms.ComboBox cboFWTargetVersion;
        private System.Windows.Forms.Label lblSalaryTargetVersion;
        private System.Windows.Forms.Label lblCoreTargetVersion;
        private System.Windows.Forms.Label lblFWTargetVersion;
        private System.Windows.Forms.ListBox lstMergeNeedsFile;
        private System.Windows.Forms.Label lblMergeNeedsFiles;
        private System.Windows.Forms.ListBox lstCustomizedFile;
        private System.Windows.Forms.Label lblCustomizedFiles;
        private System.Windows.Forms.ToolStripStatusLabel tslStatus;
        private System.Windows.Forms.Label lblCustomizedFileCount;
        private System.Windows.Forms.Button cmdShowReport;
        private System.Windows.Forms.Button cmdSearch;
        private System.Windows.Forms.Label lblFWCustomized;
        private System.Windows.Forms.Button cmdFilePrepare;
        private System.Windows.Forms.FolderBrowserDialog stfDialog;
        private System.Windows.Forms.TextBox txtYearAdjustVersion;
        private System.Windows.Forms.Label lblYearAdjustVersion;
        private System.Windows.Forms.Label lblYearAdjustTargetVersion;
        private System.Windows.Forms.ComboBox cboYearAdjustTargetVersion;
        private System.Windows.Forms.Button cmdMergeFilePrepare;
        private System.Windows.Forms.FolderBrowserDialog fldBrowser;
    }
}

