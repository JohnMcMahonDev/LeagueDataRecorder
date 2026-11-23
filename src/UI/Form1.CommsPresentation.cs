
namespace League_Data_Recorder;

public partial class Form1
{
    private DarkButton? commsShareButton;
    private IReadOnlyDictionary<string, string> displayedShareChampions = new Dictionary<string, string>();

    private void InitializeCommsShareDisplay()
    {
        commsShareButton = new DarkButton
        {
            Text = "Analyze comms", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2), Enabled = false,
            AccessibleName = "Analyze speaking time, teamfight share and callouts"
        };
        commsHeadingLayout.ColumnCount = 3;
        commsHeadingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ThemeDrawing.Px(this, 116)));
        commsHeadingLayout.Controls.Add(commsShareButton, 2, 0);
        commsShareButton.Click += async (_, _) => await ShowCommsAnalysisAsync();

        foreach (var (_, boxes) in GetDraftIconRows())
            foreach (PictureBox box in boxes)
            {
                int size = ThemeDrawing.Px(box, 44);
                box.Dock = DockStyle.None;
                box.Anchor = AnchorStyles.None;
                box.Size = new Size(size, size);
                box.MaximumSize = box.Size;
                box.Margin = new Padding(2);
                box.SizeMode = PictureBoxSizeMode.Zoom;
            }
        matchOverviewLayout.RowStyles[2].Height = ThemeDrawing.Px(matchOverviewLayout, 144);
        draftLayout.RowStyles[1].Height = 50;
        draftLayout.RowStyles[2].Height = 50;
        draftTitle.Text = "Match picks";
    }

    private void ClearCommsShares()
    {
        ResetCommsAnalysis();
        displayedShareChampions = new Dictionary<string, string>();
        if (commsShareButton is not null) commsShareButton.Enabled = false;
        draftTitle.Text = "Match picks";
    }

    private void UpdateCommsShares(TranscriptionResult? transcript, IReadOnlyDictionary<string, string>? roles,
        IReadOnlyDictionary<string, string> champions)
    {
        ClearCommsShares();
        if (transcript is null) return;
        displayedShareChampions = new Dictionary<string, string>(champions);
        SetCommsAnalysisInput(transcript, roles);
        if (commsShareButton is not null)
        {
            commsShareButton.Enabled = true;
            historyToolTip.SetToolTip(commsShareButton, "Open player speaking time, estimated teamfight share, spell callouts, jungle tracking, roams and objective calls.");
        }
    }

}
