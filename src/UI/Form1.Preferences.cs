using System.Text.Json;

namespace League_Data_Recorder;

public partial class Form1
{
    private bool appPreferencesLoaded;
    private bool appPreferencesDirty;
    private bool appPreferencesSaveError;
    private System.Windows.Forms.Timer? appPreferencesTimer;

    private static ComboBox CreateGameTypeCombo(string value)
    {
        var combo = new DarkComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDown,
            AccessibleName = "Game type", MaxLength = 80 };
        combo.Items.AddRange(GameTypes.Suggestions);
        combo.Text = GameTypes.Normalize(value);
        return combo;
    }

    private void InitializeSessionGameType()
    {
        // Use the last flexible row for the extra setting without overlapping fixed rows.
        settingsLayout.RowCount = 19;
        settingsLayout.RowStyles[16] = new RowStyle(SizeType.Absolute, 26);
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settingsLayout.Height += 64;
        sessionGameTypeCombo = CreateGameTypeCombo("Scrim");
        settingsLayout.Controls.Add(AnalysisLabel("Game type"), 0, 16);
        settingsLayout.Controls.Add(sessionGameTypeCombo, 0, 17);
        historyToolTip.SetToolTip(sessionGameTypeCombo, "Choose or type a game type. Applies to new games in this session and the Analysis filter.");
    }

    private void LoadAppPreferences()
    {
        if (appPreferencesLoaded) return;
        var saved = new AppPreferences();
        try
        {
            saved = PreferenceFiles.Load<AppPreferences>("app-settings.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { AppendToLog("Could not restore saved preferences: " + ex.Message); }
        folderTextBox.Text = saved.RecordingsFolder ?? "";
        allyTeamTextBox.Text = saved.AllyTeam ?? "Allies";
        enemyTeamTextBox.Text = saved.EnemyTeam ?? "Enemies";
        sessionGameTypeCombo.Text = GameTypes.Normalize(saved.GameType);
        recordVideoCheckbox.Checked = saved.RecordVideo;
        recordAudioCheckbox.Checked = saved.RecordAudio;
        saveReplayCheckbox.Checked = saved.SaveReplay;
        saveRawDataCheckbox.Checked = saved.SaveRawData;
        pollingIntervalInput.Value = Math.Clamp(saved.PollingIntervalMs, pollingIntervalInput.Minimum, pollingIntervalInput.Maximum);
        gameExpireTimeInput.Value = Math.Clamp(saved.GameExpirySeconds, gameExpireTimeInput.Minimum, gameExpireTimeInput.Maximum);
        appPreferencesTimer = new System.Windows.Forms.Timer(components) { Interval = 400 };
        appPreferencesTimer.Tick += (_, _) => SaveAppPreferences();
        void Changed(object? sender, EventArgs e)
        {
            appPreferencesDirty = true;
            appPreferencesTimer.Stop(); appPreferencesTimer.Start();
        }
        foreach (var text in new[] { folderTextBox, allyTeamTextBox, enemyTeamTextBox }) text.TextChanged += Changed;
        sessionGameTypeCombo.TextChanged += Changed;
        foreach (var check in new[] { recordVideoCheckbox, recordAudioCheckbox, saveReplayCheckbox, saveRawDataCheckbox })
            check.CheckedChanged += Changed;
        pollingIntervalInput.ValueChanged += Changed; gameExpireTimeInput.ValueChanged += Changed;
        appPreferencesLoaded = true;
        appPreferencesDirty = true;
        SaveAppPreferences();
    }

    private void SaveAppPreferences()
    {
        appPreferencesTimer?.Stop();
        if (!appPreferencesLoaded || !appPreferencesDirty) return;
        try
        {
            PreferenceFiles.Save("app-settings.json", new AppPreferences
            {
                RecordingsFolder = folderTextBox.Text, AllyTeam = allyTeamTextBox.Text, EnemyTeam = enemyTeamTextBox.Text,
                GameType = GameTypes.Normalize(sessionGameTypeCombo.Text), RecordVideo = recordVideoCheckbox.Checked,
                RecordAudio = recordAudioCheckbox.Checked, SaveReplay = saveReplayCheckbox.Checked, SaveRawData = saveRawDataCheckbox.Checked,
                PollingIntervalMs = pollingIntervalInput.Value, GameExpirySeconds = gameExpireTimeInput.Value,
            });
            appPreferencesDirty = appPreferencesSaveError = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!appPreferencesSaveError) AppendToLog("Preferences could not be saved: " + ex.Message);
            appPreferencesSaveError = true;
        }
    }
}
