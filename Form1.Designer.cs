#nullable disable
using System.ComponentModel;

namespace League_Data_Recorder
{
    partial class Form1
    {
        private IContainer components = null;
        private ToolTip historyToolTip;
        private TableLayoutPanel appLayout;
        private TableLayoutPanel appHeaderLayout;
        private TableLayoutPanel brandLayout;
        private Label appTitle;
        private Label appSubtitle;
        private TableLayoutPanel navigationLayout;
        private DarkButton recordingNavButton;
        private DarkButton historyNavButton;
        private DarkButton analysisNavButton;
        private DarkButton devicesNavButton;
        private WorkspaceTabs tabControl1;
        private TabPage recordingPageTab;
        private TabPage historyPageTab;
        private TabPage analysisPageTab;
        private TabPage devicesPageTab;
        private TableLayoutPanel recordingLayout;
        private DarkSurface settingsPanel;
        private TableLayoutPanel settingsLayout;
        private Label settingsTitle;
        private Label folderLabel;
        private TextBox folderTextBox;
        private DarkButton browseFolderButton;
        private Label teamsTitle;
        private TableLayoutPanel teamNamesLayout;
        private Label allyTeamLabel;
        private TextBox allyTeamTextBox;
        private Label enemyTeamLabel;
        private TextBox enemyTeamTextBox;
        private Label captureTitle;
        private TableLayoutPanel captureLayout;
        private CheckBox recordVideoCheckbox;
        private CheckBox recordAudioCheckbox;
        private CheckBox saveReplayCheckbox;
        private CheckBox saveRawDataCheckbox;
        private Label timingTitle;
        private TableLayoutPanel timingLayout;
        private Label pollingIntervalLabel;
        private Label gameExpireTimeLabel;
        private NumericUpDown pollingIntervalInput;
        private NumericUpDown gameExpireTimeInput;
        private TableLayoutPanel sessionLayout;
        private TableLayoutPanel sessionActionsLayout;
        private DarkButton startRecordingButton;
        private DarkButton stopRecordingButton;
        private TableLayoutPanel sessionStatsLayout;
        private DarkSurface statusCard;
        private TableLayoutPanel statusCardLayout;
        private Label statusCardTitle;
        private Label statusLabel;
        private DarkSurface gameCard;
        private TableLayoutPanel gameCardLayout;
        private Label gameCardTitle;
        private Label gameIdLabel;
        private DarkSurface countCard;
        private TableLayoutPanel countCardLayout;
        private Label countCardTitle;
        private Label gamesRecordedLabel;
        private DarkSurface logPanel;
        private TableLayoutPanel logLayout;
        private Label logTitle;
        private RichTextBox liveDataLog;
        private TableLayoutPanel historyLayout;
        private DarkSurface historyListPanel;
        private TableLayoutPanel historyListLayout;
        private Label historyListTitle;
        private Label historyListStatus;
        private ImageList recordingThumbnails;
        private DarkRecordingList historyScrollView;
        private ColumnHeader recordedColumn;
        private ColumnHeader durationColumn;
        private TableLayoutPanel historyDetailLayout;
        private TableLayoutPanel matchHeadingLayout;
        private Label gameTitleLabel;
        private Label gameMetadataLabel;
        private TableLayoutPanel historyToolbarLayout;
        private DarkButton transcribeButton;
        private DarkButton openReplayButton;
        private DarkButton moreActionsButton;
        private ContextMenuStrip recordingActionsMenu;
        private ToolStripMenuItem openFolderMenuItem;
        private ToolStripMenuItem deleteGameMenuItem;
        private ToolStripSeparator recordingActionsSeparator;
        private TableLayoutPanel matchOverviewLayout;
        private DarkSurface previewPanel;
        private PictureBox thumbnailPictureBox;
        private Label previewPlaceholder;
        private DarkSurface draftPanel;
        private TableLayoutPanel draftLayout;
        private Label draftTitle;
        private Label alliesLabel;
        private Label enemiesLabel;
        private TableLayoutPanel bPicksLayout;
        private PictureBox b1ChampIcon;
        private PictureBox b2ChampIcon;
        private PictureBox b3ChampIcon;
        private PictureBox b4ChampIcon;
        private PictureBox b5ChampIcon;
        private TableLayoutPanel rPicksLayout;
        private PictureBox r1ChampIcon;
        private PictureBox r2ChampIcon;
        private PictureBox r3ChampIcon;
        private PictureBox r4ChampIcon;
        private PictureBox r5ChampIcon;
        private DarkSurface commsPanel;
        private TableLayoutPanel commsLayout;
        private TableLayoutPanel commsHeadingLayout;
        private Label commsTitle;
        private DarkButton editSpeakerRolesButton;
        private Panel commsBody;
        private DarkTranscriptGrid commsGrid;
        private DataGridViewTextBoxColumn commsTimeColumn;
        private DataGridViewTextBoxColumn commsSpeakerColumn;
        private DataGridViewTextBoxColumn commsTextColumn;
        private Label commsStatusLabel;
        private Label transcriptionStatusLabel;
        private DarkProgressBar transcriptionProgressBar;
        private TableLayoutPanel devicesLayout;
        private Label devicesTitle;
        private Label devicesSubtitle;
        private DarkSurface whisperDevicePanel;
        private TableLayoutPanel whisperDeviceLayout;
        private Label whisperDeviceTitle;
        private Label whisperPurpose;
        private DarkComboBox transcriptionDeviceCombo;
        private Label whisper2Caption;
        private Label accelerationStatusLabel;
        private Label whisper3Caption;
        private Label whisperTestLabel;
        private Label whisper4Caption;
        private Label whisperJobLabel;
        private DarkSurface nemoDevicePanel;
        private TableLayoutPanel nemoDeviceLayout;
        private Label nemoDeviceTitle;
        private Label nemoPurpose;
        private DarkComboBox nemoDeviceCombo;
        private Label nemo2Caption;
        private Label nemoStatusLabel;
        private Label nemo3Caption;
        private Label nemoTestLabel;
        private Label nemo4Caption;
        private Label nemoJobLabel;
        private TableLayoutPanel devicesFooterLayout;
        private TableLayoutPanel deviceActionsLayout;
        private DarkButton checkGpuButton;
        private DarkButton gpuSetupButton;
        private Label accelerationDetailLabel;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeHistoryImages();
                DisposeAnalysisImages();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new Container();
            DataGridViewCellStyle commsRowStyle = new DataGridViewCellStyle();
            DataGridViewCellStyle commsHeaderStyle = new DataGridViewCellStyle();
            DataGridViewCellStyle commsTimeStyle = new DataGridViewCellStyle();
            DataGridViewCellStyle commsSpeakerStyle = new DataGridViewCellStyle();
            historyToolTip = new ToolTip(components);
            appLayout = new TableLayoutPanel();
            appHeaderLayout = new TableLayoutPanel();
            brandLayout = new TableLayoutPanel();
            appTitle = new Label();
            appSubtitle = new Label();
            navigationLayout = new TableLayoutPanel();
            recordingNavButton = new DarkButton();
            historyNavButton = new DarkButton();
            analysisNavButton = new DarkButton();
            devicesNavButton = new DarkButton();
            tabControl1 = new WorkspaceTabs();
            recordingPageTab = new TabPage();
            historyPageTab = new TabPage();
            analysisPageTab = new TabPage();
            devicesPageTab = new TabPage();
            recordingLayout = new TableLayoutPanel();
            settingsPanel = new DarkSurface();
            settingsLayout = new TableLayoutPanel();
            settingsTitle = new Label();
            folderLabel = new Label();
            folderTextBox = new TextBox();
            browseFolderButton = new DarkButton();
            teamsTitle = new Label();
            teamNamesLayout = new TableLayoutPanel();
            allyTeamLabel = new Label();
            allyTeamTextBox = new TextBox();
            enemyTeamLabel = new Label();
            enemyTeamTextBox = new TextBox();
            captureTitle = new Label();
            captureLayout = new TableLayoutPanel();
            recordVideoCheckbox = new CheckBox();
            recordAudioCheckbox = new CheckBox();
            saveReplayCheckbox = new CheckBox();
            saveRawDataCheckbox = new CheckBox();
            timingTitle = new Label();
            timingLayout = new TableLayoutPanel();
            pollingIntervalLabel = new Label();
            gameExpireTimeLabel = new Label();
            pollingIntervalInput = new NumericUpDown();
            gameExpireTimeInput = new NumericUpDown();
            sessionLayout = new TableLayoutPanel();
            sessionActionsLayout = new TableLayoutPanel();
            startRecordingButton = new DarkButton();
            stopRecordingButton = new DarkButton();
            sessionStatsLayout = new TableLayoutPanel();
            statusCard = new DarkSurface();
            statusCardLayout = new TableLayoutPanel();
            statusCardTitle = new Label();
            statusLabel = new Label();
            gameCard = new DarkSurface();
            gameCardLayout = new TableLayoutPanel();
            gameCardTitle = new Label();
            gameIdLabel = new Label();
            countCard = new DarkSurface();
            countCardLayout = new TableLayoutPanel();
            countCardTitle = new Label();
            gamesRecordedLabel = new Label();
            logPanel = new DarkSurface();
            logLayout = new TableLayoutPanel();
            logTitle = new Label();
            liveDataLog = new RichTextBox();
            historyLayout = new TableLayoutPanel();
            historyListPanel = new DarkSurface();
            historyListLayout = new TableLayoutPanel();
            historyListTitle = new Label();
            historyListStatus = new Label();
            recordingThumbnails = new ImageList(components);
            historyScrollView = new DarkRecordingList();
            recordedColumn = new ColumnHeader();
            durationColumn = new ColumnHeader();
            historyDetailLayout = new TableLayoutPanel();
            matchHeadingLayout = new TableLayoutPanel();
            gameTitleLabel = new Label();
            gameMetadataLabel = new Label();
            historyToolbarLayout = new TableLayoutPanel();
            transcribeButton = new DarkButton();
            openReplayButton = new DarkButton();
            moreActionsButton = new DarkButton();
            recordingActionsMenu = new ContextMenuStrip(components);
            openFolderMenuItem = new ToolStripMenuItem();
            deleteGameMenuItem = new ToolStripMenuItem();
            recordingActionsSeparator = new ToolStripSeparator();
            matchOverviewLayout = new TableLayoutPanel();
            previewPanel = new DarkSurface();
            thumbnailPictureBox = new PictureBox();
            previewPlaceholder = new Label();
            draftPanel = new DarkSurface();
            draftLayout = new TableLayoutPanel();
            draftTitle = new Label();
            alliesLabel = new Label();
            enemiesLabel = new Label();
            bPicksLayout = new TableLayoutPanel();
            b1ChampIcon = new PictureBox();
            b2ChampIcon = new PictureBox();
            b3ChampIcon = new PictureBox();
            b4ChampIcon = new PictureBox();
            b5ChampIcon = new PictureBox();
            rPicksLayout = new TableLayoutPanel();
            r1ChampIcon = new PictureBox();
            r2ChampIcon = new PictureBox();
            r3ChampIcon = new PictureBox();
            r4ChampIcon = new PictureBox();
            r5ChampIcon = new PictureBox();
            commsPanel = new DarkSurface();
            commsLayout = new TableLayoutPanel();
            commsHeadingLayout = new TableLayoutPanel();
            commsTitle = new Label();
            editSpeakerRolesButton = new DarkButton();
            commsBody = new Panel();
            commsGrid = new DarkTranscriptGrid();
            commsTimeColumn = new DataGridViewTextBoxColumn();
            commsSpeakerColumn = new DataGridViewTextBoxColumn();
            commsTextColumn = new DataGridViewTextBoxColumn();
            commsStatusLabel = new Label();
            transcriptionStatusLabel = new Label();
            transcriptionProgressBar = new DarkProgressBar();
            devicesLayout = new TableLayoutPanel();
            devicesTitle = new Label();
            devicesSubtitle = new Label();
            whisperDevicePanel = new DarkSurface();
            whisperDeviceLayout = new TableLayoutPanel();
            whisperDeviceTitle = new Label();
            whisperPurpose = new Label();
            transcriptionDeviceCombo = new DarkComboBox();
            whisper2Caption = new Label();
            accelerationStatusLabel = new Label();
            whisper3Caption = new Label();
            whisperTestLabel = new Label();
            whisper4Caption = new Label();
            whisperJobLabel = new Label();
            nemoDevicePanel = new DarkSurface();
            nemoDeviceLayout = new TableLayoutPanel();
            nemoDeviceTitle = new Label();
            nemoPurpose = new Label();
            nemoDeviceCombo = new DarkComboBox();
            nemo2Caption = new Label();
            nemoStatusLabel = new Label();
            nemo3Caption = new Label();
            nemoTestLabel = new Label();
            nemo4Caption = new Label();
            nemoJobLabel = new Label();
            devicesFooterLayout = new TableLayoutPanel();
            deviceActionsLayout = new TableLayoutPanel();
            checkGpuButton = new DarkButton();
            gpuSetupButton = new DarkButton();
            accelerationDetailLabel = new Label();
            appLayout.SuspendLayout();
            appHeaderLayout.SuspendLayout();
            brandLayout.SuspendLayout();
            navigationLayout.SuspendLayout();
            tabControl1.SuspendLayout();
            recordingPageTab.SuspendLayout();
            historyPageTab.SuspendLayout();
            devicesPageTab.SuspendLayout();
            recordingLayout.SuspendLayout();
            settingsPanel.SuspendLayout();
            settingsLayout.SuspendLayout();
            teamNamesLayout.SuspendLayout();
            captureLayout.SuspendLayout();
            timingLayout.SuspendLayout();
            sessionLayout.SuspendLayout();
            sessionActionsLayout.SuspendLayout();
            sessionStatsLayout.SuspendLayout();
            statusCard.SuspendLayout();
            statusCardLayout.SuspendLayout();
            gameCard.SuspendLayout();
            gameCardLayout.SuspendLayout();
            countCard.SuspendLayout();
            countCardLayout.SuspendLayout();
            logPanel.SuspendLayout();
            logLayout.SuspendLayout();
            historyLayout.SuspendLayout();
            historyListPanel.SuspendLayout();
            historyListLayout.SuspendLayout();
            historyDetailLayout.SuspendLayout();
            matchHeadingLayout.SuspendLayout();
            historyToolbarLayout.SuspendLayout();
            recordingActionsMenu.SuspendLayout();
            matchOverviewLayout.SuspendLayout();
            previewPanel.SuspendLayout();
            draftPanel.SuspendLayout();
            draftLayout.SuspendLayout();
            bPicksLayout.SuspendLayout();
            rPicksLayout.SuspendLayout();
            commsPanel.SuspendLayout();
            commsLayout.SuspendLayout();
            commsHeadingLayout.SuspendLayout();
            commsBody.SuspendLayout();
            devicesLayout.SuspendLayout();
            whisperDevicePanel.SuspendLayout();
            whisperDeviceLayout.SuspendLayout();
            nemoDevicePanel.SuspendLayout();
            nemoDeviceLayout.SuspendLayout();
            devicesFooterLayout.SuspendLayout();
            deviceActionsLayout.SuspendLayout();
            ((ISupportInitialize)pollingIntervalInput).BeginInit();
            ((ISupportInitialize)gameExpireTimeInput).BeginInit();
            ((ISupportInitialize)thumbnailPictureBox).BeginInit();
            ((ISupportInitialize)b1ChampIcon).BeginInit();
            ((ISupportInitialize)b2ChampIcon).BeginInit();
            ((ISupportInitialize)b3ChampIcon).BeginInit();
            ((ISupportInitialize)b4ChampIcon).BeginInit();
            ((ISupportInitialize)b5ChampIcon).BeginInit();
            ((ISupportInitialize)r1ChampIcon).BeginInit();
            ((ISupportInitialize)r2ChampIcon).BeginInit();
            ((ISupportInitialize)r3ChampIcon).BeginInit();
            ((ISupportInitialize)r4ChampIcon).BeginInit();
            ((ISupportInitialize)r5ChampIcon).BeginInit();
            ((ISupportInitialize)commsGrid).BeginInit();
            SuspendLayout();
            commsRowStyle.BackColor = Color.FromArgb(25, 29, 37);
            commsRowStyle.ForeColor = Color.FromArgb(227, 232, 241);
            commsRowStyle.SelectionBackColor = Color.FromArgb(36, 56, 82);
            commsRowStyle.SelectionForeColor = Color.White;
            commsRowStyle.Padding = new Padding(7);
            commsRowStyle.WrapMode = DataGridViewTriState.True;
            commsHeaderStyle.BackColor = Color.FromArgb(34, 40, 51);
            commsHeaderStyle.ForeColor = Color.FromArgb(157, 169, 188);
            commsHeaderStyle.SelectionBackColor = Color.FromArgb(34, 40, 51);
            commsHeaderStyle.SelectionForeColor = Color.FromArgb(157, 169, 188);
            commsHeaderStyle.Padding = new Padding(7, 0, 7, 0);
            commsTimeStyle.ForeColor = Color.FromArgb(157, 169, 188);
            commsTimeStyle.WrapMode = DataGridViewTriState.False;
            commsSpeakerStyle.ForeColor = Color.FromArgb(146, 184, 239);
            //
            // historyToolTip
            //
            historyToolTip.AutoPopDelay = 15000;
            //
            // appLayout
            //
            appLayout.Name = "appLayout";
            appLayout.Dock = DockStyle.Fill;
            appLayout.Margin = new Padding(0);
            appLayout.ColumnCount = 1;
            appLayout.RowCount = 2;
            appLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            appLayout.Size = new Size(1340, 840);
            appLayout.BackColor = Color.FromArgb(18, 21, 27);
            appLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            appLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            appLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            appLayout.Controls.Add(appHeaderLayout, 0, 0);
            appLayout.Controls.Add(tabControl1, 0, 1);
            //
            // appHeaderLayout
            //
            appHeaderLayout.Name = "appHeaderLayout";
            appHeaderLayout.Dock = DockStyle.Fill;
            appHeaderLayout.Margin = new Padding(24, 0, 24, 0);
            appHeaderLayout.ColumnCount = 2;
            appHeaderLayout.RowCount = 1;
            appHeaderLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            appHeaderLayout.BackColor = Color.FromArgb(18, 21, 27);
            appHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            appHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460F));
            appHeaderLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            appHeaderLayout.Controls.Add(brandLayout, 0, 0);
            appHeaderLayout.Controls.Add(navigationLayout, 1, 0);
            //
            // brandLayout
            //
            brandLayout.Name = "brandLayout";
            brandLayout.Dock = DockStyle.Fill;
            brandLayout.Margin = new Padding(0, 10, 0, 0);
            brandLayout.ColumnCount = 1;
            brandLayout.RowCount = 2;
            brandLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            brandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            brandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            brandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            brandLayout.Controls.Add(appTitle, 0, 0);
            brandLayout.Controls.Add(appSubtitle, 0, 1);
            //
            // appTitle
            //
            appTitle.Name = "appTitle";
            appTitle.Dock = DockStyle.Fill;
            appTitle.Margin = new Padding(0);
            appTitle.Text = "League Scrim Recorder";
            appTitle.AutoEllipsis = true;
            appTitle.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            appTitle.ForeColor = Color.FromArgb(227, 232, 241);
            appTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // appSubtitle
            //
            appSubtitle.Name = "appSubtitle";
            appSubtitle.Dock = DockStyle.Fill;
            appSubtitle.Margin = new Padding(0);
            appSubtitle.Text = "Record sessions · Review comms · Analyze performance";
            appSubtitle.AutoEllipsis = true;
            appSubtitle.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            appSubtitle.ForeColor = Color.FromArgb(157, 169, 188);
            appSubtitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // navigationLayout
            //
            navigationLayout.Name = "navigationLayout";
            navigationLayout.Dock = DockStyle.None;
            navigationLayout.Margin = new Padding(0);
            navigationLayout.ColumnCount = 4;
            navigationLayout.RowCount = 1;
            navigationLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            navigationLayout.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            navigationLayout.Size = new Size(460, 38);
            navigationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navigationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navigationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navigationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navigationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            navigationLayout.Controls.Add(recordingNavButton, 0, 0);
            navigationLayout.Controls.Add(historyNavButton, 1, 0);
            navigationLayout.Controls.Add(analysisNavButton, 2, 0);
            navigationLayout.Controls.Add(devicesNavButton, 3, 0);
            //
            // recordingNavButton
            //
            recordingNavButton.Name = "recordingNavButton";
            recordingNavButton.Dock = DockStyle.Fill;
            recordingNavButton.Margin = new Padding(0, 0, 8, 2);
            recordingNavButton.Text = "Recording";
            recordingNavButton.AccessibleName = "Recording";
            recordingNavButton.FlatStyle = FlatStyle.Flat;
            recordingNavButton.BackColor = Color.FromArgb(34, 40, 51);
            recordingNavButton.ForeColor = Color.FromArgb(227, 232, 241);
            recordingNavButton.Cursor = Cursors.Hand;
            recordingNavButton.UseVisualStyleBackColor = false;
            recordingNavButton.FlatAppearance.BorderSize = 1;
            recordingNavButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            recordingNavButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            recordingNavButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // historyNavButton
            //
            historyNavButton.Name = "historyNavButton";
            historyNavButton.Dock = DockStyle.Fill;
            historyNavButton.Margin = new Padding(0, 0, 8, 2);
            historyNavButton.Text = "History";
            historyNavButton.AccessibleName = "History";
            historyNavButton.FlatStyle = FlatStyle.Flat;
            historyNavButton.BackColor = Color.FromArgb(34, 40, 51);
            historyNavButton.ForeColor = Color.FromArgb(227, 232, 241);
            historyNavButton.Cursor = Cursors.Hand;
            historyNavButton.UseVisualStyleBackColor = false;
            historyNavButton.FlatAppearance.BorderSize = 1;
            historyNavButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            historyNavButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            historyNavButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // analysisNavButton
            //
            analysisNavButton.Name = "analysisNavButton";
            analysisNavButton.Dock = DockStyle.Fill;
            analysisNavButton.Margin = new Padding(0, 0, 8, 2);
            analysisNavButton.Text = "Analysis";
            analysisNavButton.AccessibleName = "Analysis";
            analysisNavButton.FlatStyle = FlatStyle.Flat;
            analysisNavButton.BackColor = Color.FromArgb(34, 40, 51);
            analysisNavButton.ForeColor = Color.FromArgb(227, 232, 241);
            analysisNavButton.Cursor = Cursors.Hand;
            analysisNavButton.UseVisualStyleBackColor = false;
            analysisNavButton.FlatAppearance.BorderSize = 1;
            analysisNavButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            analysisNavButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            analysisNavButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // devicesNavButton
            //
            devicesNavButton.Name = "devicesNavButton";
            devicesNavButton.Dock = DockStyle.Fill;
            devicesNavButton.Margin = new Padding(0, 0, 8, 2);
            devicesNavButton.Text = "Devices";
            devicesNavButton.AccessibleName = "Devices";
            devicesNavButton.FlatStyle = FlatStyle.Flat;
            devicesNavButton.BackColor = Color.FromArgb(34, 40, 51);
            devicesNavButton.ForeColor = Color.FromArgb(227, 232, 241);
            devicesNavButton.Cursor = Cursors.Hand;
            devicesNavButton.UseVisualStyleBackColor = false;
            devicesNavButton.FlatAppearance.BorderSize = 1;
            devicesNavButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            devicesNavButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            devicesNavButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // tabControl1
            //
            tabControl1.Name = "tabControl1";
            tabControl1.Dock = DockStyle.Fill;
            tabControl1.Margin = new Padding(20, 0, 20, 20);
            tabControl1.AccessibleName = "Workspace";
            tabControl1.SelectedIndex = 0;
            tabControl1.ItemSize = new Size(114, 32);
            tabControl1.SizeMode = TabSizeMode.Fixed;
            tabControl1.Controls.Add(recordingPageTab);
            tabControl1.Controls.Add(historyPageTab);
            tabControl1.Controls.Add(analysisPageTab);
            tabControl1.Controls.Add(devicesPageTab);
            //
            // recordingPageTab
            //
            recordingPageTab.Name = "recordingPageTab";
            recordingPageTab.Dock = DockStyle.Fill;
            recordingPageTab.Margin = new Padding(0);
            recordingPageTab.Text = "Recording";
            recordingPageTab.BackColor = Color.FromArgb(18, 21, 27);
            recordingPageTab.Padding = new Padding(0);
            recordingPageTab.Size = new Size(1292, 704);
            recordingPageTab.Controls.Add(recordingLayout);
            //
            // historyPageTab
            //
            historyPageTab.Name = "historyPageTab";
            historyPageTab.Dock = DockStyle.Fill;
            historyPageTab.Margin = new Padding(0);
            historyPageTab.Text = "History";
            historyPageTab.BackColor = Color.FromArgb(18, 21, 27);
            historyPageTab.Padding = new Padding(0);
            historyPageTab.Size = new Size(1292, 704);
            historyPageTab.Controls.Add(historyLayout);
            //
            // analysisPageTab
            //
            analysisPageTab.Name = "analysisPageTab";
            analysisPageTab.Dock = DockStyle.Fill;
            analysisPageTab.Margin = new Padding(0);
            analysisPageTab.Text = "Analysis";
            analysisPageTab.BackColor = Color.FromArgb(18, 21, 27);
            analysisPageTab.Padding = new Padding(0);
            analysisPageTab.Size = new Size(1292, 704);
            //
            // devicesPageTab
            //
            devicesPageTab.Name = "devicesPageTab";
            devicesPageTab.Dock = DockStyle.Fill;
            devicesPageTab.Margin = new Padding(0);
            devicesPageTab.Text = "Devices";
            devicesPageTab.BackColor = Color.FromArgb(18, 21, 27);
            devicesPageTab.Padding = new Padding(0);
            devicesPageTab.Size = new Size(1292, 704);
            devicesPageTab.Controls.Add(devicesLayout);
            //
            // recordingLayout
            //
            recordingLayout.Name = "recordingLayout";
            recordingLayout.Dock = DockStyle.Fill;
            recordingLayout.Margin = new Padding(0);
            recordingLayout.ColumnCount = 3;
            recordingLayout.RowCount = 1;
            recordingLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            recordingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310F));
            recordingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16F));
            recordingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            recordingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            recordingLayout.Controls.Add(settingsPanel, 0, 0);
            recordingLayout.Controls.Add(sessionLayout, 2, 0);
            //
            // settingsPanel
            //
            settingsPanel.Name = "settingsPanel";
            settingsPanel.Dock = DockStyle.Fill;
            settingsPanel.Margin = new Padding(0);
            settingsPanel.BackColor = Color.FromArgb(25, 29, 37);
            settingsPanel.Padding = new Padding(18);
            settingsPanel.AutoScroll = true;
            settingsPanel.Controls.Add(settingsLayout);
            //
            // settingsLayout
            //
            settingsLayout.Name = "settingsLayout";
            settingsLayout.Dock = DockStyle.Top;
            settingsLayout.Margin = new Padding(0);
            settingsLayout.ColumnCount = 1;
            settingsLayout.RowCount = 17;
            settingsLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            settingsLayout.Height = 540;
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            settingsLayout.Controls.Add(settingsTitle, 0, 0);
            settingsLayout.Controls.Add(folderLabel, 0, 1);
            settingsLayout.Controls.Add(folderTextBox, 0, 2);
            settingsLayout.Controls.Add(browseFolderButton, 0, 3);
            settingsLayout.Controls.Add(teamsTitle, 0, 5);
            settingsLayout.Controls.Add(teamNamesLayout, 0, 6);
            settingsLayout.SetRowSpan(teamNamesLayout, 2);
            settingsLayout.Controls.Add(captureTitle, 0, 9);
            settingsLayout.Controls.Add(captureLayout, 0, 10);
            settingsLayout.SetRowSpan(captureLayout, 2);
            settingsLayout.Controls.Add(timingTitle, 0, 13);
            settingsLayout.Controls.Add(timingLayout, 0, 14);
            settingsLayout.SetRowSpan(timingLayout, 2);
            //
            // settingsTitle
            //
            settingsTitle.Name = "settingsTitle";
            settingsTitle.Dock = DockStyle.Fill;
            settingsTitle.Margin = new Padding(0);
            settingsTitle.Text = "Session setup";
            settingsTitle.AutoEllipsis = true;
            settingsTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            settingsTitle.ForeColor = Color.FromArgb(227, 232, 241);
            settingsTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // folderLabel
            //
            folderLabel.Name = "folderLabel";
            folderLabel.Dock = DockStyle.Fill;
            folderLabel.Margin = new Padding(0);
            folderLabel.Text = "Recordings folder";
            folderLabel.AutoEllipsis = true;
            folderLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            folderLabel.ForeColor = Color.FromArgb(157, 169, 188);
            folderLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // folderTextBox
            //
            folderTextBox.Name = "folderTextBox";
            folderTextBox.Dock = DockStyle.Fill;
            folderTextBox.Margin = new Padding(0, 2, 6, 4);
            folderTextBox.Text = "";
            folderTextBox.BackColor = Color.FromArgb(20, 24, 31);
            folderTextBox.ForeColor = Color.FromArgb(227, 232, 241);
            folderTextBox.BorderStyle = BorderStyle.FixedSingle;
            folderTextBox.ReadOnly = true;
            //
            // browseFolderButton
            //
            browseFolderButton.Name = "browseFolderButton";
            browseFolderButton.Dock = DockStyle.Fill;
            browseFolderButton.Margin = new Padding(0, 0, 8, 2);
            browseFolderButton.Text = "Choose folder…";
            browseFolderButton.AccessibleName = "Choose folder…";
            browseFolderButton.FlatStyle = FlatStyle.Flat;
            browseFolderButton.BackColor = Color.FromArgb(34, 40, 51);
            browseFolderButton.ForeColor = Color.FromArgb(227, 232, 241);
            browseFolderButton.Cursor = Cursors.Hand;
            browseFolderButton.UseVisualStyleBackColor = false;
            browseFolderButton.FlatAppearance.BorderSize = 1;
            browseFolderButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            browseFolderButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            browseFolderButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // teamsTitle
            //
            teamsTitle.Name = "teamsTitle";
            teamsTitle.Dock = DockStyle.Fill;
            teamsTitle.Margin = new Padding(0);
            teamsTitle.Text = "Teams";
            teamsTitle.AutoEllipsis = true;
            teamsTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            teamsTitle.ForeColor = Color.FromArgb(227, 232, 241);
            teamsTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // teamNamesLayout
            //
            teamNamesLayout.Name = "teamNamesLayout";
            teamNamesLayout.Dock = DockStyle.Fill;
            teamNamesLayout.Margin = new Padding(0);
            teamNamesLayout.ColumnCount = 2;
            teamNamesLayout.RowCount = 2;
            teamNamesLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            teamNamesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            teamNamesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            teamNamesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            teamNamesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            teamNamesLayout.Controls.Add(allyTeamLabel, 0, 0);
            teamNamesLayout.Controls.Add(allyTeamTextBox, 0, 1);
            teamNamesLayout.Controls.Add(enemyTeamLabel, 1, 0);
            teamNamesLayout.Controls.Add(enemyTeamTextBox, 1, 1);
            //
            // allyTeamLabel
            //
            allyTeamLabel.Name = "allyTeamLabel";
            allyTeamLabel.Dock = DockStyle.Fill;
            allyTeamLabel.Margin = new Padding(0);
            allyTeamLabel.Text = "Your team";
            allyTeamLabel.AutoEllipsis = true;
            allyTeamLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            allyTeamLabel.ForeColor = Color.FromArgb(157, 169, 188);
            allyTeamLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // allyTeamTextBox
            //
            allyTeamTextBox.Name = "allyTeamTextBox";
            allyTeamTextBox.Dock = DockStyle.Fill;
            allyTeamTextBox.Margin = new Padding(0, 2, 6, 4);
            allyTeamTextBox.Text = "Allies";
            allyTeamTextBox.BackColor = Color.FromArgb(20, 24, 31);
            allyTeamTextBox.ForeColor = Color.FromArgb(227, 232, 241);
            allyTeamTextBox.BorderStyle = BorderStyle.FixedSingle;
            //
            // enemyTeamLabel
            //
            enemyTeamLabel.Name = "enemyTeamLabel";
            enemyTeamLabel.Dock = DockStyle.Fill;
            enemyTeamLabel.Margin = new Padding(0);
            enemyTeamLabel.Text = "Opponent";
            enemyTeamLabel.AutoEllipsis = true;
            enemyTeamLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            enemyTeamLabel.ForeColor = Color.FromArgb(157, 169, 188);
            enemyTeamLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // enemyTeamTextBox
            //
            enemyTeamTextBox.Name = "enemyTeamTextBox";
            enemyTeamTextBox.Dock = DockStyle.Fill;
            enemyTeamTextBox.Margin = new Padding(0, 2, 6, 4);
            enemyTeamTextBox.Text = "Enemies";
            enemyTeamTextBox.BackColor = Color.FromArgb(20, 24, 31);
            enemyTeamTextBox.ForeColor = Color.FromArgb(227, 232, 241);
            enemyTeamTextBox.BorderStyle = BorderStyle.FixedSingle;
            //
            // captureTitle
            //
            captureTitle.Name = "captureTitle";
            captureTitle.Dock = DockStyle.Fill;
            captureTitle.Margin = new Padding(0);
            captureTitle.Text = "Capture & save";
            captureTitle.AutoEllipsis = true;
            captureTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            captureTitle.ForeColor = Color.FromArgb(227, 232, 241);
            captureTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // captureLayout
            //
            captureLayout.Name = "captureLayout";
            captureLayout.Dock = DockStyle.Fill;
            captureLayout.Margin = new Padding(0);
            captureLayout.ColumnCount = 2;
            captureLayout.RowCount = 2;
            captureLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            captureLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            captureLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            captureLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            captureLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            captureLayout.Controls.Add(recordVideoCheckbox, 0, 0);
            captureLayout.Controls.Add(recordAudioCheckbox, 1, 0);
            captureLayout.Controls.Add(saveReplayCheckbox, 0, 1);
            captureLayout.Controls.Add(saveRawDataCheckbox, 1, 1);
            //
            // recordVideoCheckbox
            //
            recordVideoCheckbox.Name = "recordVideoCheckbox";
            recordVideoCheckbox.Dock = DockStyle.Fill;
            recordVideoCheckbox.Margin = new Padding(0);
            recordVideoCheckbox.Text = "Video";
            recordVideoCheckbox.Checked = true;
            recordVideoCheckbox.ForeColor = Color.FromArgb(227, 232, 241);
            //
            // recordAudioCheckbox
            //
            recordAudioCheckbox.Name = "recordAudioCheckbox";
            recordAudioCheckbox.Dock = DockStyle.Fill;
            recordAudioCheckbox.Margin = new Padding(0);
            recordAudioCheckbox.Text = "Voice comms";
            recordAudioCheckbox.Checked = true;
            recordAudioCheckbox.ForeColor = Color.FromArgb(227, 232, 241);
            //
            // saveReplayCheckbox
            //
            saveReplayCheckbox.Name = "saveReplayCheckbox";
            saveReplayCheckbox.Dock = DockStyle.Fill;
            saveReplayCheckbox.Margin = new Padding(0);
            saveReplayCheckbox.Text = "League replay";
            saveReplayCheckbox.Checked = true;
            saveReplayCheckbox.ForeColor = Color.FromArgb(227, 232, 241);
            //
            // saveRawDataCheckbox
            //
            saveRawDataCheckbox.Name = "saveRawDataCheckbox";
            saveRawDataCheckbox.Dock = DockStyle.Fill;
            saveRawDataCheckbox.Margin = new Padding(0);
            saveRawDataCheckbox.Text = "Raw game data";
            saveRawDataCheckbox.Checked = true;
            saveRawDataCheckbox.ForeColor = Color.FromArgb(227, 232, 241);
            //
            // timingTitle
            //
            timingTitle.Name = "timingTitle";
            timingTitle.Dock = DockStyle.Fill;
            timingTitle.Margin = new Padding(0);
            timingTitle.Text = "Timing";
            timingTitle.AutoEllipsis = true;
            timingTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            timingTitle.ForeColor = Color.FromArgb(227, 232, 241);
            timingTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // timingLayout
            //
            timingLayout.Name = "timingLayout";
            timingLayout.Dock = DockStyle.Fill;
            timingLayout.Margin = new Padding(0);
            timingLayout.ColumnCount = 2;
            timingLayout.RowCount = 2;
            timingLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            timingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            timingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            timingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            timingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            timingLayout.Controls.Add(pollingIntervalLabel, 0, 0);
            timingLayout.Controls.Add(gameExpireTimeLabel, 1, 0);
            timingLayout.Controls.Add(pollingIntervalInput, 0, 1);
            timingLayout.Controls.Add(gameExpireTimeInput, 1, 1);
            //
            // pollingIntervalLabel
            //
            pollingIntervalLabel.Name = "pollingIntervalLabel";
            pollingIntervalLabel.Dock = DockStyle.Fill;
            pollingIntervalLabel.Margin = new Padding(0);
            pollingIntervalLabel.Text = "Polling (ms)";
            pollingIntervalLabel.AutoEllipsis = true;
            pollingIntervalLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            pollingIntervalLabel.ForeColor = Color.FromArgb(157, 169, 188);
            pollingIntervalLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // gameExpireTimeLabel
            //
            gameExpireTimeLabel.Name = "gameExpireTimeLabel";
            gameExpireTimeLabel.Dock = DockStyle.Fill;
            gameExpireTimeLabel.Margin = new Padding(0);
            gameExpireTimeLabel.Text = "Game expiry (s)";
            gameExpireTimeLabel.AutoEllipsis = true;
            gameExpireTimeLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            gameExpireTimeLabel.ForeColor = Color.FromArgb(157, 169, 188);
            gameExpireTimeLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // pollingIntervalInput
            //
            pollingIntervalInput.Name = "pollingIntervalInput";
            pollingIntervalInput.Dock = DockStyle.Fill;
            pollingIntervalInput.Margin = new Padding(0, 2, 6, 4);
            pollingIntervalInput.Minimum = 250;
            pollingIntervalInput.Maximum = 10000;
            pollingIntervalInput.Value = 1000;
            pollingIntervalInput.Increment = 250;
            pollingIntervalInput.BackColor = Color.FromArgb(20, 24, 31);
            pollingIntervalInput.ForeColor = Color.FromArgb(227, 232, 241);
            pollingIntervalInput.BorderStyle = BorderStyle.FixedSingle;
            //
            // gameExpireTimeInput
            //
            gameExpireTimeInput.Name = "gameExpireTimeInput";
            gameExpireTimeInput.Dock = DockStyle.Fill;
            gameExpireTimeInput.Margin = new Padding(0, 2, 6, 4);
            gameExpireTimeInput.Minimum = 60;
            gameExpireTimeInput.Maximum = 600;
            gameExpireTimeInput.Value = 300;
            gameExpireTimeInput.Increment = 30;
            gameExpireTimeInput.BackColor = Color.FromArgb(20, 24, 31);
            gameExpireTimeInput.ForeColor = Color.FromArgb(227, 232, 241);
            gameExpireTimeInput.BorderStyle = BorderStyle.FixedSingle;
            //
            // sessionLayout
            //
            sessionLayout.Name = "sessionLayout";
            sessionLayout.Dock = DockStyle.Fill;
            sessionLayout.Margin = new Padding(0);
            sessionLayout.ColumnCount = 1;
            sessionLayout.RowCount = 4;
            sessionLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            sessionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sessionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            sessionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F));
            sessionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            sessionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            sessionLayout.Controls.Add(sessionActionsLayout, 0, 0);
            sessionLayout.Controls.Add(sessionStatsLayout, 0, 1);
            sessionLayout.Controls.Add(logPanel, 0, 3);
            //
            // sessionActionsLayout
            //
            sessionActionsLayout.Name = "sessionActionsLayout";
            sessionActionsLayout.Dock = DockStyle.Fill;
            sessionActionsLayout.Margin = new Padding(0);
            sessionActionsLayout.ColumnCount = 3;
            sessionActionsLayout.RowCount = 1;
            sessionActionsLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            sessionActionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168F));
            sessionActionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            sessionActionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sessionActionsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            sessionActionsLayout.Controls.Add(startRecordingButton, 0, 0);
            sessionActionsLayout.Controls.Add(stopRecordingButton, 1, 0);
            //
            // startRecordingButton
            //
            startRecordingButton.Name = "startRecordingButton";
            startRecordingButton.Dock = DockStyle.Fill;
            startRecordingButton.Margin = new Padding(0, 0, 8, 2);
            startRecordingButton.Text = "Start recording";
            startRecordingButton.AccessibleName = "Start recording";
            startRecordingButton.FlatStyle = FlatStyle.Flat;
            startRecordingButton.BackColor = Color.FromArgb(54, 101, 170);
            startRecordingButton.ForeColor = Color.FromArgb(227, 232, 241);
            startRecordingButton.Cursor = Cursors.Hand;
            startRecordingButton.UseVisualStyleBackColor = false;
            startRecordingButton.FlatAppearance.BorderSize = 0;
            startRecordingButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            startRecordingButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(66, 118, 194);
            startRecordingButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // stopRecordingButton
            //
            stopRecordingButton.Name = "stopRecordingButton";
            stopRecordingButton.Dock = DockStyle.Fill;
            stopRecordingButton.Margin = new Padding(0, 0, 8, 2);
            stopRecordingButton.Text = "Stop recording";
            stopRecordingButton.AccessibleName = "Stop recording";
            stopRecordingButton.FlatStyle = FlatStyle.Flat;
            stopRecordingButton.BackColor = Color.FromArgb(34, 40, 51);
            stopRecordingButton.ForeColor = Color.FromArgb(227, 232, 241);
            stopRecordingButton.Cursor = Cursors.Hand;
            stopRecordingButton.UseVisualStyleBackColor = false;
            stopRecordingButton.FlatAppearance.BorderSize = 1;
            stopRecordingButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            stopRecordingButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            stopRecordingButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // sessionStatsLayout
            //
            sessionStatsLayout.Name = "sessionStatsLayout";
            sessionStatsLayout.Dock = DockStyle.Fill;
            sessionStatsLayout.Margin = new Padding(0);
            sessionStatsLayout.ColumnCount = 3;
            sessionStatsLayout.RowCount = 1;
            sessionStatsLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            sessionStatsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            sessionStatsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            sessionStatsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            sessionStatsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            sessionStatsLayout.Controls.Add(statusCard, 0, 0);
            sessionStatsLayout.Controls.Add(gameCard, 1, 0);
            sessionStatsLayout.Controls.Add(countCard, 2, 0);
            //
            // statusCard
            //
            statusCard.Name = "statusCard";
            statusCard.Dock = DockStyle.Fill;
            statusCard.Margin = new Padding(0, 0, 0, 0);
            statusCard.BackColor = Color.FromArgb(25, 29, 37);
            statusCard.Padding = new Padding(16, 8, 12, 12);
            statusCard.Controls.Add(statusCardLayout);
            //
            // statusCardLayout
            //
            statusCardLayout.Name = "statusCardLayout";
            statusCardLayout.Dock = DockStyle.Fill;
            statusCardLayout.Margin = new Padding(0);
            statusCardLayout.ColumnCount = 1;
            statusCardLayout.RowCount = 2;
            statusCardLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            statusCardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusCardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            statusCardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            statusCardLayout.Controls.Add(statusCardTitle, 0, 0);
            statusCardLayout.Controls.Add(statusLabel, 0, 1);
            //
            // statusCardTitle
            //
            statusCardTitle.Name = "statusCardTitle";
            statusCardTitle.Dock = DockStyle.Fill;
            statusCardTitle.Margin = new Padding(0);
            statusCardTitle.Text = "SESSION";
            statusCardTitle.AutoEllipsis = true;
            statusCardTitle.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
            statusCardTitle.ForeColor = Color.FromArgb(157, 169, 188);
            statusCardTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // statusLabel
            //
            statusLabel.Name = "statusLabel";
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Margin = new Padding(0);
            statusLabel.Text = "Not recording";
            statusLabel.AutoEllipsis = true;
            statusLabel.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            statusLabel.ForeColor = Color.FromArgb(227, 232, 241);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // gameCard
            //
            gameCard.Name = "gameCard";
            gameCard.Dock = DockStyle.Fill;
            gameCard.Margin = new Padding(10, 0, 0, 0);
            gameCard.BackColor = Color.FromArgb(25, 29, 37);
            gameCard.Padding = new Padding(16, 8, 12, 12);
            gameCard.Controls.Add(gameCardLayout);
            //
            // gameCardLayout
            //
            gameCardLayout.Name = "gameCardLayout";
            gameCardLayout.Dock = DockStyle.Fill;
            gameCardLayout.Margin = new Padding(0);
            gameCardLayout.ColumnCount = 1;
            gameCardLayout.RowCount = 2;
            gameCardLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            gameCardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            gameCardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            gameCardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            gameCardLayout.Controls.Add(gameCardTitle, 0, 0);
            gameCardLayout.Controls.Add(gameIdLabel, 0, 1);
            //
            // gameCardTitle
            //
            gameCardTitle.Name = "gameCardTitle";
            gameCardTitle.Dock = DockStyle.Fill;
            gameCardTitle.Margin = new Padding(0);
            gameCardTitle.Text = "CURRENT GAME";
            gameCardTitle.AutoEllipsis = true;
            gameCardTitle.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
            gameCardTitle.ForeColor = Color.FromArgb(157, 169, 188);
            gameCardTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // gameIdLabel
            //
            gameIdLabel.Name = "gameIdLabel";
            gameIdLabel.Dock = DockStyle.Fill;
            gameIdLabel.Margin = new Padding(0);
            gameIdLabel.Text = "—";
            gameIdLabel.AutoEllipsis = true;
            gameIdLabel.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            gameIdLabel.ForeColor = Color.FromArgb(227, 232, 241);
            gameIdLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // countCard
            //
            countCard.Name = "countCard";
            countCard.Dock = DockStyle.Fill;
            countCard.Margin = new Padding(10, 0, 0, 0);
            countCard.BackColor = Color.FromArgb(25, 29, 37);
            countCard.Padding = new Padding(16, 8, 12, 12);
            countCard.Controls.Add(countCardLayout);
            //
            // countCardLayout
            //
            countCardLayout.Name = "countCardLayout";
            countCardLayout.Dock = DockStyle.Fill;
            countCardLayout.Margin = new Padding(0);
            countCardLayout.ColumnCount = 1;
            countCardLayout.RowCount = 2;
            countCardLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            countCardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            countCardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            countCardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            countCardLayout.Controls.Add(countCardTitle, 0, 0);
            countCardLayout.Controls.Add(gamesRecordedLabel, 0, 1);
            //
            // countCardTitle
            //
            countCardTitle.Name = "countCardTitle";
            countCardTitle.Dock = DockStyle.Fill;
            countCardTitle.Margin = new Padding(0);
            countCardTitle.Text = "SAVED GAMES";
            countCardTitle.AutoEllipsis = true;
            countCardTitle.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
            countCardTitle.ForeColor = Color.FromArgb(157, 169, 188);
            countCardTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // gamesRecordedLabel
            //
            gamesRecordedLabel.Name = "gamesRecordedLabel";
            gamesRecordedLabel.Dock = DockStyle.Fill;
            gamesRecordedLabel.Margin = new Padding(0);
            gamesRecordedLabel.Text = "0";
            gamesRecordedLabel.AutoEllipsis = true;
            gamesRecordedLabel.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            gamesRecordedLabel.ForeColor = Color.FromArgb(227, 232, 241);
            gamesRecordedLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // logPanel
            //
            logPanel.Name = "logPanel";
            logPanel.Dock = DockStyle.Fill;
            logPanel.Margin = new Padding(0);
            logPanel.BackColor = Color.FromArgb(25, 29, 37);
            logPanel.Padding = new Padding(18, 10, 18, 16);
            logPanel.Controls.Add(logLayout);
            //
            // logLayout
            //
            logLayout.Name = "logLayout";
            logLayout.Dock = DockStyle.Fill;
            logLayout.Margin = new Padding(0);
            logLayout.ColumnCount = 1;
            logLayout.RowCount = 2;
            logLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            logLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            logLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            logLayout.Controls.Add(logTitle, 0, 0);
            logLayout.Controls.Add(liveDataLog, 0, 1);
            //
            // logTitle
            //
            logTitle.Name = "logTitle";
            logTitle.Dock = DockStyle.Fill;
            logTitle.Margin = new Padding(0);
            logTitle.Text = "Activity";
            logTitle.AutoEllipsis = true;
            logTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            logTitle.ForeColor = Color.FromArgb(227, 232, 241);
            logTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // liveDataLog
            //
            liveDataLog.Name = "liveDataLog";
            liveDataLog.Dock = DockStyle.Fill;
            liveDataLog.Margin = new Padding(0);
            liveDataLog.BorderStyle = BorderStyle.None;
            liveDataLog.ReadOnly = true;
            liveDataLog.BackColor = Color.FromArgb(25, 29, 37);
            liveDataLog.ForeColor = Color.FromArgb(157, 169, 188);
            liveDataLog.Font = new Font("Consolas", 9F);
            liveDataLog.WordWrap = true;
            liveDataLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            liveDataLog.AccessibleName = "Activity log";
            //
            // historyLayout
            //
            historyLayout.Name = "historyLayout";
            historyLayout.Dock = DockStyle.Fill;
            historyLayout.Margin = new Padding(0);
            historyLayout.ColumnCount = 3;
            historyLayout.RowCount = 1;
            historyLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 292F));
            historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16F));
            historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            historyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            historyLayout.Controls.Add(historyListPanel, 0, 0);
            historyLayout.Controls.Add(historyDetailLayout, 2, 0);
            //
            // historyListPanel
            //
            historyListPanel.Name = "historyListPanel";
            historyListPanel.Dock = DockStyle.Fill;
            historyListPanel.Margin = new Padding(0);
            historyListPanel.BackColor = Color.FromArgb(25, 29, 37);
            historyListPanel.Padding = new Padding(14, 10, 14, 14);
            historyListPanel.Controls.Add(historyListLayout);
            //
            // historyListLayout
            //
            historyListLayout.Name = "historyListLayout";
            historyListLayout.Dock = DockStyle.Fill;
            historyListLayout.Margin = new Padding(0);
            historyListLayout.ColumnCount = 1;
            historyListLayout.RowCount = 3;
            historyListLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            historyListLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            historyListLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            historyListLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            historyListLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            historyListLayout.Controls.Add(historyListTitle, 0, 0);
            historyListLayout.Controls.Add(historyListStatus, 0, 1);
            historyListLayout.Controls.Add(historyScrollView, 0, 2);
            //
            // historyListTitle
            //
            historyListTitle.Name = "historyListTitle";
            historyListTitle.Dock = DockStyle.Fill;
            historyListTitle.Margin = new Padding(0);
            historyListTitle.Text = "Recordings";
            historyListTitle.AutoEllipsis = true;
            historyListTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            historyListTitle.ForeColor = Color.FromArgb(227, 232, 241);
            historyListTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // historyListStatus
            //
            historyListStatus.Name = "historyListStatus";
            historyListStatus.Dock = DockStyle.Fill;
            historyListStatus.Margin = new Padding(0);
            historyListStatus.Text = "Choose a recordings folder to get started.";
            historyListStatus.AutoEllipsis = true;
            historyListStatus.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            historyListStatus.ForeColor = Color.FromArgb(157, 169, 188);
            historyListStatus.TextAlign = ContentAlignment.MiddleLeft;
            //
            // recordingThumbnails
            //
            recordingThumbnails.ColorDepth = ColorDepth.Depth32Bit;
            recordingThumbnails.ImageSize = new Size(76, 44);
            //
            // historyScrollView
            //
            historyScrollView.Name = "historyScrollView";
            historyScrollView.Dock = DockStyle.Fill;
            historyScrollView.Margin = new Padding(0);
            historyScrollView.BorderStyle = BorderStyle.None;
            historyScrollView.BackColor = Color.FromArgb(25, 29, 37);
            historyScrollView.ForeColor = Color.FromArgb(227, 232, 241);
            historyScrollView.LargeImageList = recordingThumbnails;
            historyScrollView.View = View.Tile;
            historyScrollView.MultiSelect = false;
            historyScrollView.HideSelection = false;
            historyScrollView.ShowItemToolTips = true;
            historyScrollView.TileSize = new Size(248, 76);
            historyScrollView.UseCompatibleStateImageBehavior = false;
            historyScrollView.AccessibleName = "Saved recordings";
            historyScrollView.Columns.AddRange(new ColumnHeader[] { recordedColumn, durationColumn });
            //
            // recordedColumn
            //
            recordedColumn.Name = "recordedColumn";
            recordedColumn.Text = "Recorded";
            //
            // durationColumn
            //
            durationColumn.Name = "durationColumn";
            durationColumn.Text = "Duration";
            //
            // historyDetailLayout
            //
            historyDetailLayout.Name = "historyDetailLayout";
            historyDetailLayout.Dock = DockStyle.Fill;
            historyDetailLayout.Margin = new Padding(0);
            historyDetailLayout.ColumnCount = 1;
            historyDetailLayout.RowCount = 7;
            historyDetailLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            historyDetailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 152F));
            historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            historyDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 5F));
            historyDetailLayout.Controls.Add(matchHeadingLayout, 0, 0);
            historyDetailLayout.Controls.Add(historyToolbarLayout, 0, 1);
            historyDetailLayout.Controls.Add(matchOverviewLayout, 0, 2);
            historyDetailLayout.Controls.Add(commsPanel, 0, 4);
            historyDetailLayout.Controls.Add(transcriptionStatusLabel, 0, 5);
            historyDetailLayout.Controls.Add(transcriptionProgressBar, 0, 6);
            //
            // matchHeadingLayout
            //
            matchHeadingLayout.Name = "matchHeadingLayout";
            matchHeadingLayout.Dock = DockStyle.Fill;
            matchHeadingLayout.Margin = new Padding(0);
            matchHeadingLayout.ColumnCount = 1;
            matchHeadingLayout.RowCount = 2;
            matchHeadingLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            matchHeadingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            matchHeadingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            matchHeadingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            matchHeadingLayout.Controls.Add(gameTitleLabel, 0, 0);
            matchHeadingLayout.Controls.Add(gameMetadataLabel, 0, 1);
            //
            // gameTitleLabel
            //
            gameTitleLabel.Name = "gameTitleLabel";
            gameTitleLabel.Dock = DockStyle.Fill;
            gameTitleLabel.Margin = new Padding(0);
            gameTitleLabel.Text = "Select a recording";
            gameTitleLabel.AutoEllipsis = true;
            gameTitleLabel.Font = new Font("Segoe UI", 17F, FontStyle.Bold);
            gameTitleLabel.ForeColor = Color.FromArgb(227, 232, 241);
            gameTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // gameMetadataLabel
            //
            gameMetadataLabel.Name = "gameMetadataLabel";
            gameMetadataLabel.Dock = DockStyle.Fill;
            gameMetadataLabel.Margin = new Padding(0);
            gameMetadataLabel.Text = "Your saved scrims and team comms, in one place.";
            gameMetadataLabel.AutoEllipsis = true;
            gameMetadataLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            gameMetadataLabel.ForeColor = Color.FromArgb(157, 169, 188);
            gameMetadataLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // historyToolbarLayout
            //
            historyToolbarLayout.Name = "historyToolbarLayout";
            historyToolbarLayout.Dock = DockStyle.Fill;
            historyToolbarLayout.Margin = new Padding(0);
            historyToolbarLayout.ColumnCount = 4;
            historyToolbarLayout.RowCount = 1;
            historyToolbarLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            historyToolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142F));
            historyToolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124F));
            historyToolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            historyToolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            historyToolbarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            historyToolbarLayout.Controls.Add(transcribeButton, 0, 0);
            historyToolbarLayout.Controls.Add(openReplayButton, 1, 0);
            historyToolbarLayout.Controls.Add(moreActionsButton, 2, 0);
            //
            // transcribeButton
            //
            transcribeButton.Name = "transcribeButton";
            transcribeButton.Dock = DockStyle.Fill;
            transcribeButton.Margin = new Padding(0, 0, 8, 2);
            transcribeButton.Text = "Transcribe";
            transcribeButton.AccessibleName = "Transcribe";
            transcribeButton.FlatStyle = FlatStyle.Flat;
            transcribeButton.BackColor = Color.FromArgb(54, 101, 170);
            transcribeButton.ForeColor = Color.FromArgb(227, 232, 241);
            transcribeButton.Cursor = Cursors.Hand;
            transcribeButton.UseVisualStyleBackColor = false;
            transcribeButton.FlatAppearance.BorderSize = 0;
            transcribeButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            transcribeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(66, 118, 194);
            transcribeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // openReplayButton
            //
            openReplayButton.Name = "openReplayButton";
            openReplayButton.Dock = DockStyle.Fill;
            openReplayButton.Margin = new Padding(0, 0, 8, 2);
            openReplayButton.Text = "Open replay";
            openReplayButton.AccessibleName = "Open replay";
            openReplayButton.FlatStyle = FlatStyle.Flat;
            openReplayButton.BackColor = Color.FromArgb(34, 40, 51);
            openReplayButton.ForeColor = Color.FromArgb(227, 232, 241);
            openReplayButton.Cursor = Cursors.Hand;
            openReplayButton.UseVisualStyleBackColor = false;
            openReplayButton.FlatAppearance.BorderSize = 1;
            openReplayButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            openReplayButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            openReplayButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // moreActionsButton
            //
            moreActionsButton.Name = "moreActionsButton";
            moreActionsButton.Dock = DockStyle.Fill;
            moreActionsButton.Margin = new Padding(0, 0, 8, 2);
            moreActionsButton.Text = "More  ▾";
            moreActionsButton.AccessibleName = "More  ▾";
            moreActionsButton.FlatStyle = FlatStyle.Flat;
            moreActionsButton.BackColor = Color.FromArgb(34, 40, 51);
            moreActionsButton.ForeColor = Color.FromArgb(227, 232, 241);
            moreActionsButton.Cursor = Cursors.Hand;
            moreActionsButton.UseVisualStyleBackColor = false;
            moreActionsButton.FlatAppearance.BorderSize = 1;
            moreActionsButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            moreActionsButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            moreActionsButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // recordingActionsMenu
            //
            recordingActionsMenu.Name = "recordingActionsMenu";
            recordingActionsMenu.BackColor = Color.FromArgb(34, 40, 51);
            recordingActionsMenu.ForeColor = Color.FromArgb(227, 232, 241);
            recordingActionsMenu.Items.AddRange(new ToolStripItem[] { openFolderMenuItem, recordingActionsSeparator, deleteGameMenuItem });
            //
            // openFolderMenuItem
            //
            openFolderMenuItem.Name = "openFolderMenuItem";
            openFolderMenuItem.Text = "Open recording folder";
            //
            // deleteGameMenuItem
            //
            deleteGameMenuItem.Name = "deleteGameMenuItem";
            deleteGameMenuItem.Text = "Delete recording…";
            deleteGameMenuItem.ForeColor = Color.FromArgb(235, 147, 155);
            //
            // recordingActionsSeparator
            //
            recordingActionsSeparator.Name = "recordingActionsSeparator";
            //
            // matchOverviewLayout
            //
            matchOverviewLayout.Name = "matchOverviewLayout";
            matchOverviewLayout.Dock = DockStyle.Fill;
            matchOverviewLayout.Margin = new Padding(0);
            matchOverviewLayout.ColumnCount = 3;
            matchOverviewLayout.RowCount = 1;
            matchOverviewLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            matchOverviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            matchOverviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12F));
            matchOverviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            matchOverviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            matchOverviewLayout.Controls.Add(previewPanel, 0, 0);
            matchOverviewLayout.Controls.Add(draftPanel, 2, 0);
            //
            // previewPanel
            //
            previewPanel.Name = "previewPanel";
            previewPanel.Dock = DockStyle.Fill;
            previewPanel.Margin = new Padding(0);
            previewPanel.BackColor = Color.FromArgb(25, 29, 37);
            previewPanel.Padding = new Padding(0);
            previewPanel.Controls.Add(previewPlaceholder);
            previewPanel.Controls.Add(thumbnailPictureBox);
            //
            // thumbnailPictureBox
            //
            thumbnailPictureBox.Name = "thumbnailPictureBox";
            thumbnailPictureBox.Dock = DockStyle.Fill;
            thumbnailPictureBox.Margin = new Padding(0);
            thumbnailPictureBox.BackColor = Color.FromArgb(20, 24, 31);
            thumbnailPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            thumbnailPictureBox.TabStop = false;
            thumbnailPictureBox.AccessibleName = "Video preview";
            //
            // previewPlaceholder
            //
            previewPlaceholder.Name = "previewPlaceholder";
            previewPlaceholder.Dock = DockStyle.Fill;
            previewPlaceholder.Margin = new Padding(0);
            previewPlaceholder.Text = "Video preview";
            previewPlaceholder.AutoEllipsis = true;
            previewPlaceholder.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            previewPlaceholder.ForeColor = Color.FromArgb(157, 169, 188);
            previewPlaceholder.TextAlign = ContentAlignment.MiddleCenter;
            previewPlaceholder.BackColor = Color.FromArgb(20, 24, 31);
            //
            // draftPanel
            //
            draftPanel.Name = "draftPanel";
            draftPanel.Dock = DockStyle.Fill;
            draftPanel.Margin = new Padding(0);
            draftPanel.BackColor = Color.FromArgb(25, 29, 37);
            draftPanel.Padding = new Padding(14, 6, 14, 8);
            draftPanel.Controls.Add(draftLayout);
            //
            // draftLayout
            //
            draftLayout.Name = "draftLayout";
            draftLayout.Dock = DockStyle.Fill;
            draftLayout.Margin = new Padding(0);
            draftLayout.ColumnCount = 2;
            draftLayout.RowCount = 3;
            draftLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            draftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55F));
            draftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            draftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            draftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            draftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            draftLayout.Controls.Add(draftTitle, 0, 0);
            draftLayout.SetColumnSpan(draftTitle, 2);
            draftLayout.Controls.Add(alliesLabel, 0, 1);
            draftLayout.Controls.Add(enemiesLabel, 0, 2);
            draftLayout.Controls.Add(bPicksLayout, 1, 1);
            draftLayout.Controls.Add(rPicksLayout, 1, 2);
            //
            // draftTitle
            //
            draftTitle.Name = "draftTitle";
            draftTitle.Dock = DockStyle.Fill;
            draftTitle.Margin = new Padding(0);
            draftTitle.Text = "Match picks";
            draftTitle.AutoEllipsis = true;
            draftTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            draftTitle.ForeColor = Color.FromArgb(227, 232, 241);
            draftTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // alliesLabel
            //
            alliesLabel.Name = "alliesLabel";
            alliesLabel.Dock = DockStyle.Fill;
            alliesLabel.Margin = new Padding(0);
            alliesLabel.Text = "Allies";
            alliesLabel.AutoEllipsis = true;
            alliesLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            alliesLabel.ForeColor = Color.FromArgb(146, 184, 239);
            alliesLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // enemiesLabel
            //
            enemiesLabel.Name = "enemiesLabel";
            enemiesLabel.Dock = DockStyle.Fill;
            enemiesLabel.Margin = new Padding(0);
            enemiesLabel.Text = "Enemy";
            enemiesLabel.AutoEllipsis = true;
            enemiesLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            enemiesLabel.ForeColor = Color.FromArgb(235, 147, 155);
            enemiesLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // bPicksLayout
            //
            bPicksLayout.Name = "bPicksLayout";
            bPicksLayout.Dock = DockStyle.Fill;
            bPicksLayout.Margin = new Padding(0);
            bPicksLayout.ColumnCount = 5;
            bPicksLayout.RowCount = 1;
            bPicksLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            bPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            bPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            bPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            bPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            bPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            bPicksLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            bPicksLayout.Controls.Add(b1ChampIcon, 0, 0);
            bPicksLayout.Controls.Add(b2ChampIcon, 1, 0);
            bPicksLayout.Controls.Add(b3ChampIcon, 2, 0);
            bPicksLayout.Controls.Add(b4ChampIcon, 3, 0);
            bPicksLayout.Controls.Add(b5ChampIcon, 4, 0);
            //
            // b1ChampIcon
            //
            b1ChampIcon.Name = "b1ChampIcon";
            b1ChampIcon.Dock = DockStyle.Fill;
            b1ChampIcon.Margin = new Padding(2, 5, 2, 5);
            b1ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            b1ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            b1ChampIcon.TabStop = false;
            //
            // b2ChampIcon
            //
            b2ChampIcon.Name = "b2ChampIcon";
            b2ChampIcon.Dock = DockStyle.Fill;
            b2ChampIcon.Margin = new Padding(2, 5, 2, 5);
            b2ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            b2ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            b2ChampIcon.TabStop = false;
            //
            // b3ChampIcon
            //
            b3ChampIcon.Name = "b3ChampIcon";
            b3ChampIcon.Dock = DockStyle.Fill;
            b3ChampIcon.Margin = new Padding(2, 5, 2, 5);
            b3ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            b3ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            b3ChampIcon.TabStop = false;
            //
            // b4ChampIcon
            //
            b4ChampIcon.Name = "b4ChampIcon";
            b4ChampIcon.Dock = DockStyle.Fill;
            b4ChampIcon.Margin = new Padding(2, 5, 2, 5);
            b4ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            b4ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            b4ChampIcon.TabStop = false;
            //
            // b5ChampIcon
            //
            b5ChampIcon.Name = "b5ChampIcon";
            b5ChampIcon.Dock = DockStyle.Fill;
            b5ChampIcon.Margin = new Padding(2, 5, 2, 5);
            b5ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            b5ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            b5ChampIcon.TabStop = false;
            //
            // rPicksLayout
            //
            rPicksLayout.Name = "rPicksLayout";
            rPicksLayout.Dock = DockStyle.Fill;
            rPicksLayout.Margin = new Padding(0);
            rPicksLayout.ColumnCount = 5;
            rPicksLayout.RowCount = 1;
            rPicksLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            rPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            rPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            rPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            rPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            rPicksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            rPicksLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rPicksLayout.Controls.Add(r1ChampIcon, 0, 0);
            rPicksLayout.Controls.Add(r2ChampIcon, 1, 0);
            rPicksLayout.Controls.Add(r3ChampIcon, 2, 0);
            rPicksLayout.Controls.Add(r4ChampIcon, 3, 0);
            rPicksLayout.Controls.Add(r5ChampIcon, 4, 0);
            //
            // r1ChampIcon
            //
            r1ChampIcon.Name = "r1ChampIcon";
            r1ChampIcon.Dock = DockStyle.Fill;
            r1ChampIcon.Margin = new Padding(2, 5, 2, 5);
            r1ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            r1ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            r1ChampIcon.TabStop = false;
            //
            // r2ChampIcon
            //
            r2ChampIcon.Name = "r2ChampIcon";
            r2ChampIcon.Dock = DockStyle.Fill;
            r2ChampIcon.Margin = new Padding(2, 5, 2, 5);
            r2ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            r2ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            r2ChampIcon.TabStop = false;
            //
            // r3ChampIcon
            //
            r3ChampIcon.Name = "r3ChampIcon";
            r3ChampIcon.Dock = DockStyle.Fill;
            r3ChampIcon.Margin = new Padding(2, 5, 2, 5);
            r3ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            r3ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            r3ChampIcon.TabStop = false;
            //
            // r4ChampIcon
            //
            r4ChampIcon.Name = "r4ChampIcon";
            r4ChampIcon.Dock = DockStyle.Fill;
            r4ChampIcon.Margin = new Padding(2, 5, 2, 5);
            r4ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            r4ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            r4ChampIcon.TabStop = false;
            //
            // r5ChampIcon
            //
            r5ChampIcon.Name = "r5ChampIcon";
            r5ChampIcon.Dock = DockStyle.Fill;
            r5ChampIcon.Margin = new Padding(2, 5, 2, 5);
            r5ChampIcon.SizeMode = PictureBoxSizeMode.Zoom;
            r5ChampIcon.BackColor = Color.FromArgb(20, 24, 31);
            r5ChampIcon.TabStop = false;
            //
            // commsPanel
            //
            commsPanel.Name = "commsPanel";
            commsPanel.Dock = DockStyle.Fill;
            commsPanel.Margin = new Padding(0);
            commsPanel.BackColor = Color.FromArgb(25, 29, 37);
            commsPanel.Padding = new Padding(14, 8, 14, 12);
            commsPanel.Controls.Add(commsLayout);
            //
            // commsLayout
            //
            commsLayout.Name = "commsLayout";
            commsLayout.Dock = DockStyle.Fill;
            commsLayout.Margin = new Padding(0);
            commsLayout.ColumnCount = 1;
            commsLayout.RowCount = 2;
            commsLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            commsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            commsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            commsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            commsLayout.Controls.Add(commsHeadingLayout, 0, 0);
            commsLayout.Controls.Add(commsBody, 0, 1);
            //
            // commsHeadingLayout
            //
            commsHeadingLayout.Name = "commsHeadingLayout";
            commsHeadingLayout.Dock = DockStyle.Fill;
            commsHeadingLayout.Margin = new Padding(0);
            commsHeadingLayout.ColumnCount = 2;
            commsHeadingLayout.RowCount = 1;
            commsHeadingLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            commsHeadingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            commsHeadingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114F));
            commsHeadingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            commsHeadingLayout.Controls.Add(commsTitle, 0, 0);
            commsHeadingLayout.Controls.Add(editSpeakerRolesButton, 1, 0);
            //
            // commsTitle
            //
            commsTitle.Name = "commsTitle";
            commsTitle.Dock = DockStyle.Fill;
            commsTitle.Margin = new Padding(0);
            commsTitle.Text = "Team comms";
            commsTitle.AutoEllipsis = true;
            commsTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            commsTitle.ForeColor = Color.FromArgb(227, 232, 241);
            commsTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // editSpeakerRolesButton
            //
            editSpeakerRolesButton.Name = "editSpeakerRolesButton";
            editSpeakerRolesButton.Dock = DockStyle.Fill;
            editSpeakerRolesButton.Margin = new Padding(0, 0, 8, 2);
            editSpeakerRolesButton.Text = "Edit roles…";
            editSpeakerRolesButton.AccessibleName = "Edit roles…";
            editSpeakerRolesButton.FlatStyle = FlatStyle.Flat;
            editSpeakerRolesButton.BackColor = Color.FromArgb(34, 40, 51);
            editSpeakerRolesButton.ForeColor = Color.FromArgb(227, 232, 241);
            editSpeakerRolesButton.Cursor = Cursors.Hand;
            editSpeakerRolesButton.UseVisualStyleBackColor = false;
            editSpeakerRolesButton.Enabled = false;
            editSpeakerRolesButton.FlatAppearance.BorderSize = 1;
            editSpeakerRolesButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            editSpeakerRolesButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            editSpeakerRolesButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // commsBody
            //
            commsBody.Name = "commsBody";
            commsBody.Dock = DockStyle.Fill;
            commsBody.Margin = new Padding(0);
            commsBody.Controls.Add(commsGrid);
            commsBody.Controls.Add(commsStatusLabel);
            //
            // commsGrid
            //
            commsGrid.Name = "commsGrid";
            commsGrid.Dock = DockStyle.Fill;
            commsGrid.Margin = new Padding(0);
            commsGrid.AllowUserToAddRows = false;
            commsGrid.AllowUserToDeleteRows = false;
            commsGrid.AllowUserToResizeColumns = false;
            commsGrid.AllowUserToResizeRows = false;
            commsGrid.ReadOnly = true;
            commsGrid.RowHeadersVisible = false;
            commsGrid.BorderStyle = BorderStyle.None;
            commsGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            commsGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            commsGrid.EnableHeadersVisualStyles = false;
            commsGrid.BackgroundColor = Color.FromArgb(25, 29, 37);
            commsGrid.GridColor = Color.FromArgb(49, 57, 71);
            commsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(20, 24, 31);
            commsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            commsGrid.ScrollBars = ScrollBars.Vertical;
            commsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders;
            commsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            commsGrid.ColumnHeadersHeight = 36;
            commsGrid.Visible = false;
            commsGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            commsGrid.AccessibleName = "Recording comms transcript";
            commsGrid.DefaultCellStyle = commsRowStyle;
            commsGrid.ColumnHeadersDefaultCellStyle = commsHeaderStyle;
            commsGrid.Columns.AddRange(new DataGridViewColumn[] { commsTimeColumn, commsSpeakerColumn, commsTextColumn });
            //
            // commsTimeColumn
            //
            commsTimeColumn.Name = "commsTimeColumn";
            commsTimeColumn.HeaderText = "TIME";
            commsTimeColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            commsTimeColumn.ReadOnly = true;
            commsTimeColumn.Width = 64;
            commsTimeColumn.DefaultCellStyle = commsTimeStyle;
            //
            // commsSpeakerColumn
            //
            commsSpeakerColumn.Name = "commsSpeakerColumn";
            commsSpeakerColumn.HeaderText = "ROLE";
            commsSpeakerColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            commsSpeakerColumn.ReadOnly = true;
            commsSpeakerColumn.Width = 100;
            commsSpeakerColumn.DefaultCellStyle = commsSpeakerStyle;
            //
            // commsTextColumn
            //
            commsTextColumn.Name = "commsTextColumn";
            commsTextColumn.HeaderText = "TRANSCRIPT";
            commsTextColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            commsTextColumn.ReadOnly = true;
            commsTextColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            commsTextColumn.MinimumWidth = 80;
            //
            // commsStatusLabel
            //
            commsStatusLabel.Name = "commsStatusLabel";
            commsStatusLabel.Dock = DockStyle.Fill;
            commsStatusLabel.Margin = new Padding(0);
            commsStatusLabel.Text = "Select a recording to view its comms.";
            commsStatusLabel.AutoEllipsis = true;
            commsStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            commsStatusLabel.ForeColor = Color.FromArgb(157, 169, 188);
            commsStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
            //
            // transcriptionStatusLabel
            //
            transcriptionStatusLabel.Name = "transcriptionStatusLabel";
            transcriptionStatusLabel.Dock = DockStyle.Fill;
            transcriptionStatusLabel.Margin = new Padding(0);
            transcriptionStatusLabel.Text = "Ready. Device preferences and tests are on the Devices tab.";
            transcriptionStatusLabel.AutoEllipsis = true;
            transcriptionStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            transcriptionStatusLabel.ForeColor = Color.FromArgb(157, 169, 188);
            transcriptionStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // transcriptionProgressBar
            //
            transcriptionProgressBar.Name = "transcriptionProgressBar";
            transcriptionProgressBar.Dock = DockStyle.Fill;
            transcriptionProgressBar.Margin = new Padding(0);
            transcriptionProgressBar.Style = ProgressBarStyle.Marquee;
            transcriptionProgressBar.BackColor = Color.FromArgb(49, 57, 71);
            transcriptionProgressBar.ForeColor = Color.FromArgb(146, 184, 239);
            transcriptionProgressBar.Visible = false;
            transcriptionProgressBar.AccessibleName = "Transcription progress";
            //
            // devicesLayout
            //
            devicesLayout.Name = "devicesLayout";
            devicesLayout.Dock = DockStyle.Fill;
            devicesLayout.Margin = new Padding(0);
            devicesLayout.ColumnCount = 1;
            devicesLayout.RowCount = 7;
            devicesLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            devicesLayout.Padding = new Padding(2);
            devicesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            devicesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            devicesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            devicesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 176F));
            devicesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            devicesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 176F));
            devicesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            devicesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            devicesLayout.Controls.Add(devicesTitle, 0, 0);
            devicesLayout.Controls.Add(devicesSubtitle, 0, 1);
            devicesLayout.Controls.Add(whisperDevicePanel, 0, 2);
            devicesLayout.Controls.Add(nemoDevicePanel, 0, 4);
            devicesLayout.Controls.Add(devicesFooterLayout, 0, 6);
            //
            // devicesTitle
            //
            devicesTitle.Name = "devicesTitle";
            devicesTitle.Dock = DockStyle.Fill;
            devicesTitle.Margin = new Padding(0);
            devicesTitle.Text = "Transcription devices";
            devicesTitle.AutoEllipsis = true;
            devicesTitle.Font = new Font("Segoe UI", 19F, FontStyle.Bold);
            devicesTitle.ForeColor = Color.FromArgb(227, 232, 241);
            devicesTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // devicesSubtitle
            //
            devicesSubtitle.Name = "devicesSubtitle";
            devicesSubtitle.Dock = DockStyle.Fill;
            devicesSubtitle.Margin = new Padding(0);
            devicesSubtitle.Text = "Set each model's device preference, then run a model test to verify its backend.";
            devicesSubtitle.AutoEllipsis = true;
            devicesSubtitle.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            devicesSubtitle.ForeColor = Color.FromArgb(157, 169, 188);
            devicesSubtitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // whisperDevicePanel
            //
            whisperDevicePanel.Name = "whisperDevicePanel";
            whisperDevicePanel.Dock = DockStyle.Fill;
            whisperDevicePanel.Margin = new Padding(0);
            whisperDevicePanel.BackColor = Color.FromArgb(25, 29, 37);
            whisperDevicePanel.Padding = new Padding(18, 12, 18, 12);
            whisperDevicePanel.Controls.Add(whisperDeviceLayout);
            //
            // whisperDeviceLayout
            //
            whisperDeviceLayout.Name = "whisperDeviceLayout";
            whisperDeviceLayout.Dock = DockStyle.Fill;
            whisperDeviceLayout.Margin = new Padding(0);
            whisperDeviceLayout.ColumnCount = 3;
            whisperDeviceLayout.RowCount = 5;
            whisperDeviceLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            whisperDeviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146F));
            whisperDeviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            whisperDeviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 202F));
            whisperDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            whisperDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            whisperDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            whisperDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            whisperDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            whisperDeviceLayout.Controls.Add(whisperDeviceTitle, 0, 0);
            whisperDeviceLayout.Controls.Add(whisperPurpose, 1, 0);
            whisperDeviceLayout.Controls.Add(transcriptionDeviceCombo, 2, 0);
            whisperDeviceLayout.Controls.Add(whisper2Caption, 0, 2);
            whisperDeviceLayout.Controls.Add(accelerationStatusLabel, 1, 2);
            whisperDeviceLayout.SetColumnSpan(accelerationStatusLabel, 2);
            whisperDeviceLayout.Controls.Add(whisper3Caption, 0, 3);
            whisperDeviceLayout.Controls.Add(whisperTestLabel, 1, 3);
            whisperDeviceLayout.SetColumnSpan(whisperTestLabel, 2);
            whisperDeviceLayout.Controls.Add(whisper4Caption, 0, 4);
            whisperDeviceLayout.Controls.Add(whisperJobLabel, 1, 4);
            whisperDeviceLayout.SetColumnSpan(whisperJobLabel, 2);
            //
            // whisperDeviceTitle
            //
            whisperDeviceTitle.Name = "whisperDeviceTitle";
            whisperDeviceTitle.Dock = DockStyle.Fill;
            whisperDeviceTitle.Margin = new Padding(0);
            whisperDeviceTitle.Text = "Whisper";
            whisperDeviceTitle.AutoEllipsis = true;
            whisperDeviceTitle.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            whisperDeviceTitle.ForeColor = Color.FromArgb(227, 232, 241);
            whisperDeviceTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // whisperPurpose
            //
            whisperPurpose.Name = "whisperPurpose";
            whisperPurpose.Dock = DockStyle.Fill;
            whisperPurpose.Margin = new Padding(0);
            whisperPurpose.Text = "Speech recognition + native word alignment";
            whisperPurpose.AutoEllipsis = true;
            whisperPurpose.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            whisperPurpose.ForeColor = Color.FromArgb(157, 169, 188);
            whisperPurpose.TextAlign = ContentAlignment.MiddleLeft;
            //
            // transcriptionDeviceCombo
            //
            transcriptionDeviceCombo.Name = "transcriptionDeviceCombo";
            transcriptionDeviceCombo.Dock = DockStyle.Fill;
            transcriptionDeviceCombo.Margin = new Padding(0, 3, 0, 5);
            transcriptionDeviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            transcriptionDeviceCombo.FlatStyle = FlatStyle.Flat;
            transcriptionDeviceCombo.BackColor = Color.FromArgb(20, 24, 31);
            transcriptionDeviceCombo.ForeColor = Color.FromArgb(227, 232, 241);
            transcriptionDeviceCombo.AccessibleName = "Whisper device preference";
            transcriptionDeviceCombo.Items.AddRange(new object[] { "Auto · prefer GPU", "CPU only" });
            transcriptionDeviceCombo.SelectedIndex = 0;
            //
            // whisper2Caption
            //
            whisper2Caption.Name = "whisper2Caption";
            whisper2Caption.Dock = DockStyle.Fill;
            whisper2Caption.Margin = new Padding(0);
            whisper2Caption.Text = "Availability";
            whisper2Caption.AutoEllipsis = true;
            whisper2Caption.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            whisper2Caption.ForeColor = Color.FromArgb(157, 169, 188);
            whisper2Caption.TextAlign = ContentAlignment.MiddleLeft;
            //
            // accelerationStatusLabel
            //
            accelerationStatusLabel.Name = "accelerationStatusLabel";
            accelerationStatusLabel.Dock = DockStyle.Fill;
            accelerationStatusLabel.Margin = new Padding(0);
            accelerationStatusLabel.Text = "Checking hardware and runtime…";
            accelerationStatusLabel.AutoEllipsis = true;
            accelerationStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            accelerationStatusLabel.ForeColor = Color.FromArgb(157, 169, 188);
            accelerationStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // whisper3Caption
            //
            whisper3Caption.Name = "whisper3Caption";
            whisper3Caption.Dock = DockStyle.Fill;
            whisper3Caption.Margin = new Padding(0);
            whisper3Caption.Text = "Last model test";
            whisper3Caption.AutoEllipsis = true;
            whisper3Caption.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            whisper3Caption.ForeColor = Color.FromArgb(157, 169, 188);
            whisper3Caption.TextAlign = ContentAlignment.MiddleLeft;
            //
            // whisperTestLabel
            //
            whisperTestLabel.Name = "whisperTestLabel";
            whisperTestLabel.Dock = DockStyle.Fill;
            whisperTestLabel.Margin = new Padding(0);
            whisperTestLabel.Text = "Not tested in this session";
            whisperTestLabel.AutoEllipsis = true;
            whisperTestLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            whisperTestLabel.ForeColor = Color.FromArgb(157, 169, 188);
            whisperTestLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // whisper4Caption
            //
            whisper4Caption.Name = "whisper4Caption";
            whisper4Caption.Dock = DockStyle.Fill;
            whisper4Caption.Margin = new Padding(0);
            whisper4Caption.Text = "Last transcription";
            whisper4Caption.AutoEllipsis = true;
            whisper4Caption.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            whisper4Caption.ForeColor = Color.FromArgb(157, 169, 188);
            whisper4Caption.TextAlign = ContentAlignment.MiddleLeft;
            //
            // whisperJobLabel
            //
            whisperJobLabel.Name = "whisperJobLabel";
            whisperJobLabel.Dock = DockStyle.Fill;
            whisperJobLabel.Margin = new Padding(0);
            whisperJobLabel.Text = "No inference in this session";
            whisperJobLabel.AutoEllipsis = true;
            whisperJobLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            whisperJobLabel.ForeColor = Color.FromArgb(157, 169, 188);
            whisperJobLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // nemoDevicePanel
            //
            nemoDevicePanel.Name = "nemoDevicePanel";
            nemoDevicePanel.Dock = DockStyle.Fill;
            nemoDevicePanel.Margin = new Padding(0);
            nemoDevicePanel.BackColor = Color.FromArgb(25, 29, 37);
            nemoDevicePanel.Padding = new Padding(18, 12, 18, 12);
            nemoDevicePanel.Controls.Add(nemoDeviceLayout);
            //
            // nemoDeviceLayout
            //
            nemoDeviceLayout.Name = "nemoDeviceLayout";
            nemoDeviceLayout.Dock = DockStyle.Fill;
            nemoDeviceLayout.Margin = new Padding(0);
            nemoDeviceLayout.ColumnCount = 3;
            nemoDeviceLayout.RowCount = 5;
            nemoDeviceLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            nemoDeviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146F));
            nemoDeviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            nemoDeviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 202F));
            nemoDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            nemoDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            nemoDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            nemoDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            nemoDeviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            nemoDeviceLayout.Controls.Add(nemoDeviceTitle, 0, 0);
            nemoDeviceLayout.Controls.Add(nemoPurpose, 1, 0);
            nemoDeviceLayout.Controls.Add(nemoDeviceCombo, 2, 0);
            nemoDeviceLayout.Controls.Add(nemo2Caption, 0, 2);
            nemoDeviceLayout.Controls.Add(nemoStatusLabel, 1, 2);
            nemoDeviceLayout.SetColumnSpan(nemoStatusLabel, 2);
            nemoDeviceLayout.Controls.Add(nemo3Caption, 0, 3);
            nemoDeviceLayout.Controls.Add(nemoTestLabel, 1, 3);
            nemoDeviceLayout.SetColumnSpan(nemoTestLabel, 2);
            nemoDeviceLayout.Controls.Add(nemo4Caption, 0, 4);
            nemoDeviceLayout.Controls.Add(nemoJobLabel, 1, 4);
            nemoDeviceLayout.SetColumnSpan(nemoJobLabel, 2);
            //
            // nemoDeviceTitle
            //
            nemoDeviceTitle.Name = "nemoDeviceTitle";
            nemoDeviceTitle.Dock = DockStyle.Fill;
            nemoDeviceTitle.Margin = new Padding(0);
            nemoDeviceTitle.Text = "NeMo";
            nemoDeviceTitle.AutoEllipsis = true;
            nemoDeviceTitle.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            nemoDeviceTitle.ForeColor = Color.FromArgb(227, 232, 241);
            nemoDeviceTitle.TextAlign = ContentAlignment.MiddleLeft;
            //
            // nemoPurpose
            //
            nemoPurpose.Name = "nemoPurpose";
            nemoPurpose.Dock = DockStyle.Fill;
            nemoPurpose.Margin = new Padding(0);
            nemoPurpose.Text = "Speaker diarization";
            nemoPurpose.AutoEllipsis = true;
            nemoPurpose.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            nemoPurpose.ForeColor = Color.FromArgb(157, 169, 188);
            nemoPurpose.TextAlign = ContentAlignment.MiddleLeft;
            //
            // nemoDeviceCombo
            //
            nemoDeviceCombo.Name = "nemoDeviceCombo";
            nemoDeviceCombo.Dock = DockStyle.Fill;
            nemoDeviceCombo.Margin = new Padding(0, 3, 0, 5);
            nemoDeviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            nemoDeviceCombo.FlatStyle = FlatStyle.Flat;
            nemoDeviceCombo.BackColor = Color.FromArgb(20, 24, 31);
            nemoDeviceCombo.ForeColor = Color.FromArgb(227, 232, 241);
            nemoDeviceCombo.AccessibleName = "NeMo device preference";
            nemoDeviceCombo.Items.AddRange(new object[] { "Auto · prefer GPU", "CPU only" });
            nemoDeviceCombo.SelectedIndex = 0;
            //
            // nemo2Caption
            //
            nemo2Caption.Name = "nemo2Caption";
            nemo2Caption.Dock = DockStyle.Fill;
            nemo2Caption.Margin = new Padding(0);
            nemo2Caption.Text = "Availability";
            nemo2Caption.AutoEllipsis = true;
            nemo2Caption.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            nemo2Caption.ForeColor = Color.FromArgb(157, 169, 188);
            nemo2Caption.TextAlign = ContentAlignment.MiddleLeft;
            //
            // nemoStatusLabel
            //
            nemoStatusLabel.Name = "nemoStatusLabel";
            nemoStatusLabel.Dock = DockStyle.Fill;
            nemoStatusLabel.Margin = new Padding(0);
            nemoStatusLabel.Text = "Checking hardware and runtime…";
            nemoStatusLabel.AutoEllipsis = true;
            nemoStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            nemoStatusLabel.ForeColor = Color.FromArgb(157, 169, 188);
            nemoStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // nemo3Caption
            //
            nemo3Caption.Name = "nemo3Caption";
            nemo3Caption.Dock = DockStyle.Fill;
            nemo3Caption.Margin = new Padding(0);
            nemo3Caption.Text = "Last model test";
            nemo3Caption.AutoEllipsis = true;
            nemo3Caption.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            nemo3Caption.ForeColor = Color.FromArgb(157, 169, 188);
            nemo3Caption.TextAlign = ContentAlignment.MiddleLeft;
            //
            // nemoTestLabel
            //
            nemoTestLabel.Name = "nemoTestLabel";
            nemoTestLabel.Dock = DockStyle.Fill;
            nemoTestLabel.Margin = new Padding(0);
            nemoTestLabel.Text = "Not tested in this session";
            nemoTestLabel.AutoEllipsis = true;
            nemoTestLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            nemoTestLabel.ForeColor = Color.FromArgb(157, 169, 188);
            nemoTestLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // nemo4Caption
            //
            nemo4Caption.Name = "nemo4Caption";
            nemo4Caption.Dock = DockStyle.Fill;
            nemo4Caption.Margin = new Padding(0);
            nemo4Caption.Text = "Last transcription";
            nemo4Caption.AutoEllipsis = true;
            nemo4Caption.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            nemo4Caption.ForeColor = Color.FromArgb(157, 169, 188);
            nemo4Caption.TextAlign = ContentAlignment.MiddleLeft;
            //
            // nemoJobLabel
            //
            nemoJobLabel.Name = "nemoJobLabel";
            nemoJobLabel.Dock = DockStyle.Fill;
            nemoJobLabel.Margin = new Padding(0);
            nemoJobLabel.Text = "No inference in this session";
            nemoJobLabel.AutoEllipsis = true;
            nemoJobLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            nemoJobLabel.ForeColor = Color.FromArgb(157, 169, 188);
            nemoJobLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // devicesFooterLayout
            //
            devicesFooterLayout.Name = "devicesFooterLayout";
            devicesFooterLayout.Dock = DockStyle.Fill;
            devicesFooterLayout.Margin = new Padding(0);
            devicesFooterLayout.ColumnCount = 1;
            devicesFooterLayout.RowCount = 3;
            devicesFooterLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            devicesFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            devicesFooterLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            devicesFooterLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            devicesFooterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            devicesFooterLayout.Controls.Add(deviceActionsLayout, 0, 0);
            devicesFooterLayout.Controls.Add(accelerationDetailLabel, 0, 1);
            //
            // deviceActionsLayout
            //
            deviceActionsLayout.Name = "deviceActionsLayout";
            deviceActionsLayout.Dock = DockStyle.Fill;
            deviceActionsLayout.Margin = new Padding(0);
            deviceActionsLayout.ColumnCount = 3;
            deviceActionsLayout.RowCount = 1;
            deviceActionsLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            deviceActionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            deviceActionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192F));
            deviceActionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            deviceActionsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            deviceActionsLayout.Controls.Add(checkGpuButton, 0, 0);
            deviceActionsLayout.Controls.Add(gpuSetupButton, 1, 0);
            //
            // checkGpuButton
            //
            checkGpuButton.Name = "checkGpuButton";
            checkGpuButton.Dock = DockStyle.Fill;
            checkGpuButton.Margin = new Padding(0, 0, 8, 2);
            checkGpuButton.Text = "Refresh availability";
            checkGpuButton.AccessibleName = "Refresh availability";
            checkGpuButton.FlatStyle = FlatStyle.Flat;
            checkGpuButton.BackColor = Color.FromArgb(34, 40, 51);
            checkGpuButton.ForeColor = Color.FromArgb(227, 232, 241);
            checkGpuButton.Cursor = Cursors.Hand;
            checkGpuButton.UseVisualStyleBackColor = false;
            checkGpuButton.FlatAppearance.BorderSize = 1;
            checkGpuButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            checkGpuButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 65);
            checkGpuButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // gpuSetupButton
            //
            gpuSetupButton.Name = "gpuSetupButton";
            gpuSetupButton.Dock = DockStyle.Fill;
            gpuSetupButton.Margin = new Padding(0, 0, 8, 2);
            gpuSetupButton.Text = "Model tests & setup…";
            gpuSetupButton.AccessibleName = "Model tests & setup…";
            gpuSetupButton.FlatStyle = FlatStyle.Flat;
            gpuSetupButton.BackColor = Color.FromArgb(54, 101, 170);
            gpuSetupButton.ForeColor = Color.FromArgb(227, 232, 241);
            gpuSetupButton.Cursor = Cursors.Hand;
            gpuSetupButton.UseVisualStyleBackColor = false;
            gpuSetupButton.FlatAppearance.BorderSize = 0;
            gpuSetupButton.FlatAppearance.BorderColor = Color.FromArgb(49, 57, 71);
            gpuSetupButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(66, 118, 194);
            gpuSetupButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 56, 82);
            //
            // accelerationDetailLabel
            //
            accelerationDetailLabel.Name = "accelerationDetailLabel";
            accelerationDetailLabel.Dock = DockStyle.Fill;
            accelerationDetailLabel.Margin = new Padding(0);
            accelerationDetailLabel.Text = "Availability, model tests, and jobs are tracked separately. CPU activity during VAD, decoding, and cleanup is normal.";
            accelerationDetailLabel.AutoEllipsis = false;
            accelerationDetailLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            accelerationDetailLabel.ForeColor = Color.FromArgb(157, 169, 188);
            accelerationDetailLabel.TextAlign = ContentAlignment.MiddleLeft;
            recordingNavButton.Click += recordingNavButton_Click;
            historyNavButton.Click += historyNavButton_Click;
            analysisNavButton.Click += analysisNavButton_Click;
            devicesNavButton.Click += devicesNavButton_Click;
            tabControl1.SelectedIndexChanged += tabControl1_SelectedIndexChanged;
            tabControl1.Selected += tabControl1_Selected;
            browseFolderButton.Click += browseFolderButton_Click;
            startRecordingButton.Click += startRecordingButton_Click;
            stopRecordingButton.Click += stopRecordingButton_Click;
            historyScrollView.SelectedIndexChanged += historyScrollView_SelectedIndexChanged;
            historyScrollView.ClientSizeChanged += historyScrollView_ClientSizeChanged;
            transcribeButton.Click += transcribeButton_Click;
            openReplayButton.Click += openReplayButton_Click;
            moreActionsButton.Click += moreActionsButton_Click;
            openFolderMenuItem.Click += openFileLocationButton_Click;
            deleteGameMenuItem.Click += deleteGameButton_Click;
            editSpeakerRolesButton.Click += editSpeakerRolesButton_Click;
            commsGrid.CellClick += commsGrid_CellClick;
            commsGrid.KeyDown += commsGrid_KeyDown;
            commsGrid.CellMouseEnter += commsGrid_CellMouseEnter;
            commsGrid.CellMouseLeave += commsGrid_CellMouseLeave;
            historyToolTip.SetToolTip(editSpeakerRolesButton, "Edit this game's speakers, including Coach. You can also click a Role cell.");
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(18, 21, 27);
            ForeColor = Color.FromArgb(227, 232, 241);
            ClientSize = new Size(1340, 840);
            MinimumSize = new Size(1080, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "League Scrim Recorder";
            Name = "Form1";
            Controls.Add(appLayout);
            HandleCreated += Form1_HandleCreated;
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            ((ISupportInitialize)pollingIntervalInput).EndInit();
            ((ISupportInitialize)gameExpireTimeInput).EndInit();
            ((ISupportInitialize)thumbnailPictureBox).EndInit();
            ((ISupportInitialize)b1ChampIcon).EndInit();
            ((ISupportInitialize)b2ChampIcon).EndInit();
            ((ISupportInitialize)b3ChampIcon).EndInit();
            ((ISupportInitialize)b4ChampIcon).EndInit();
            ((ISupportInitialize)b5ChampIcon).EndInit();
            ((ISupportInitialize)r1ChampIcon).EndInit();
            ((ISupportInitialize)r2ChampIcon).EndInit();
            ((ISupportInitialize)r3ChampIcon).EndInit();
            ((ISupportInitialize)r4ChampIcon).EndInit();
            ((ISupportInitialize)r5ChampIcon).EndInit();
            ((ISupportInitialize)commsGrid).EndInit();
            deviceActionsLayout.ResumeLayout(false);
            devicesFooterLayout.ResumeLayout(false);
            nemoDeviceLayout.ResumeLayout(false);
            nemoDevicePanel.ResumeLayout(false);
            whisperDeviceLayout.ResumeLayout(false);
            whisperDevicePanel.ResumeLayout(false);
            devicesLayout.ResumeLayout(false);
            commsBody.ResumeLayout(false);
            commsHeadingLayout.ResumeLayout(false);
            commsLayout.ResumeLayout(false);
            commsPanel.ResumeLayout(false);
            rPicksLayout.ResumeLayout(false);
            bPicksLayout.ResumeLayout(false);
            draftLayout.ResumeLayout(false);
            draftPanel.ResumeLayout(false);
            previewPanel.ResumeLayout(false);
            matchOverviewLayout.ResumeLayout(false);
            recordingActionsMenu.ResumeLayout(false);
            historyToolbarLayout.ResumeLayout(false);
            matchHeadingLayout.ResumeLayout(false);
            historyDetailLayout.ResumeLayout(false);
            historyListLayout.ResumeLayout(false);
            historyListPanel.ResumeLayout(false);
            historyLayout.ResumeLayout(false);
            logLayout.ResumeLayout(false);
            logPanel.ResumeLayout(false);
            countCardLayout.ResumeLayout(false);
            countCard.ResumeLayout(false);
            gameCardLayout.ResumeLayout(false);
            gameCard.ResumeLayout(false);
            statusCardLayout.ResumeLayout(false);
            statusCard.ResumeLayout(false);
            sessionStatsLayout.ResumeLayout(false);
            sessionActionsLayout.ResumeLayout(false);
            sessionLayout.ResumeLayout(false);
            timingLayout.ResumeLayout(false);
            captureLayout.ResumeLayout(false);
            teamNamesLayout.ResumeLayout(false);
            settingsLayout.ResumeLayout(false);
            settingsPanel.ResumeLayout(false);
            recordingLayout.ResumeLayout(false);
            devicesPageTab.ResumeLayout(false);
            historyPageTab.ResumeLayout(false);
            recordingPageTab.ResumeLayout(false);
            tabControl1.ResumeLayout(false);
            navigationLayout.ResumeLayout(false);
            brandLayout.ResumeLayout(false);
            appHeaderLayout.ResumeLayout(false);
            appLayout.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
    }
}
