namespace Liakont.Agent.Installer.Wizard;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Profiles;

/// <summary>
/// Wizard GUI d'installation de l'agent (F13 §4) : écrans guidés source (test ODBC lecture seule),
/// serveur central (heartbeat à blanc), options, et résumé (installation + check-config affiché). Les
/// écrans ne portent AUCUNE logique : ils collectent la saisie et délèguent tout au
/// <see cref="InstallerEngine"/> — le MÊME moteur que le mode silencieux (F13 §3). La visibilité de chaque
/// champ (affiché / verrouillé / masqué) est gouvernée par le profil intégrateur, jamais par une branche
/// codée en dur (F13 §5.1). Messages 100 % français, orientés intégrateur (CLAUDE.md n°12). Les contrôles
/// sont la propriété du Form (libérés par sa destruction) ; la saisie est lue par closures, pas par champs.
/// </summary>
internal sealed class WizardForm : Form
{
    private readonly InstallerEngine _engine;
    private readonly IntegratorProfile _profile;
    private readonly IReadOnlyList<string> _knownAdapters;

    public WizardForm(InstallerEngine engine, IntegratorProfile profile, IReadOnlyList<string> knownAdapters)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _knownAdapters = knownAdapters ?? throw new ArgumentNullException(nameof(knownAdapters));
        BuildUi();
    }

    private static TabPage BuildInstanceTab(
        InstallerEngine engine,
        IntegratorProfileEngine profileEngine,
        Dictionary<string, Func<string?>> valueReaders)
    {
        var page = new TabPage("1. Instance");
        var grid = NewGrid();

        var detected = new ListBox { Dock = DockStyle.Fill, Height = 90, IntegralHeight = false };
        try
        {
            IReadOnlyList<string> installed = engine.ListInstalledInstances();
            if (installed.Count == 0)
            {
                detected.Items.Add("(aucune instance installée sur ce poste)");
            }
            else
            {
                detected.Items.AddRange(installed.Cast<object>().ToArray());
            }
        }
        catch (InvalidOperationException ex)
        {
            detected.Items.Add("Détection des instances impossible : " + ex.Message);
        }

        AddRow(grid, "Instances déjà installées", detected);

        ResolvedField instanceField = profileEngine.Resolve(ProfileFieldKeys.InstanceName);
        if (instanceField.IsVisible)
        {
            var instanceBox = new TextBox { Dock = DockStyle.Fill };
            ApplyState(instanceBox, instanceField);
            if (instanceField.IsEditable)
            {
                valueReaders[ProfileFieldKeys.InstanceName] = () => instanceBox.Text;
            }

            AddRow(grid, "Nom de la nouvelle instance", instanceBox);

            var feedback = new Label { Dock = DockStyle.Fill, AutoSize = false, Height = 40 };
            var verify = new Button { Text = "Vérifier le nom", AutoSize = true };
            verify.Click += (sender, args) =>
            {
                feedback.Text = engine.TryValidateNewInstanceName(instanceBox.Text, out _, out string? error)
                    ? "Nom disponible."
                    : error;
            };
            AddRow(grid, string.Empty, verify);
            AddRow(grid, string.Empty, feedback);
        }

        page.Controls.Add(grid);
        return page;
    }

    private static TabPage BuildSourceTab(
        InstallerEngine engine,
        IReadOnlyList<string> knownAdapters,
        IntegratorProfileEngine profileEngine,
        Dictionary<string, Func<string?>> valueReaders)
    {
        var page = new TabPage("2. Base source");
        var grid = NewGrid();

        ResolvedField adapterField = profileEngine.Resolve(ProfileFieldKeys.Adapter);
        if (adapterField.IsVisible)
        {
            var adapterBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            adapterBox.Items.AddRange(knownAdapters.Cast<object>().ToArray());
            string? preset = adapterField.DefaultValue;
            if (preset != null && adapterBox.Items.Contains(preset))
            {
                adapterBox.SelectedItem = preset;
            }
            else if (adapterBox.Items.Count > 0)
            {
                adapterBox.SelectedIndex = 0;
            }

            adapterBox.Enabled = adapterField.IsEditable;
            if (adapterField.IsEditable)
            {
                valueReaders[ProfileFieldKeys.Adapter] = () => adapterBox.SelectedItem as string;
            }

            AddRow(grid, "Adaptateur source", adapterBox);
        }

        var odbcBox = new TextBox { Dock = DockStyle.Fill };
        ResolvedField odbcField = profileEngine.Resolve(ProfileFieldKeys.OdbcConnection);
        ApplyState(odbcBox, odbcField);
        if (odbcField.IsEditable)
        {
            valueReaders[ProfileFieldKeys.OdbcConnection] = () => odbcBox.Text;
        }

        AddRow(grid, "Connexion ODBC (DSN ou chaîne)", odbcBox);

        var result = new Label { Dock = DockStyle.Fill, AutoSize = false, Height = 60 };
        var test = new Button { Text = "Tester (lecture seule)", AutoSize = true };
        test.Click += (sender, args) =>
        {
            SourceTestResult outcome = engine.TestSource(odbcBox.Text);
            result.Text = outcome.Message;
        };
        AddRow(grid, string.Empty, test);
        AddRow(grid, "Diagnostic", result);

        page.Controls.Add(grid);
        return page;
    }

    private static TabPage BuildServerTab(
        InstallerEngine engine,
        IntegratorProfileEngine profileEngine,
        Dictionary<string, Func<string?>> valueReaders)
    {
        var page = new TabPage("3. Serveur central");
        var grid = NewGrid();

        var urlBox = new TextBox { Dock = DockStyle.Fill };
        ResolvedField urlField = profileEngine.Resolve(ProfileFieldKeys.PlatformUrl);
        ApplyState(urlBox, urlField);
        if (urlField.IsEditable)
        {
            valueReaders[ProfileFieldKeys.PlatformUrl] = () => urlBox.Text;
        }

        AddRow(grid, "URL de la plateforme", urlBox);

        // apiKey est un secret : toujours affiché (jamais imposé par le profil, F13 §6), masqué à la saisie.
        var keyBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        valueReaders[ProfileFieldKeys.ApiKey] = () => keyBox.Text;
        AddRow(grid, "Clé API de l'agent", keyBox);

        var result = new Label { Dock = DockStyle.Fill, AutoSize = false, Height = 60 };
        var test = new Button { Text = "Tester (heartbeat à blanc)", AutoSize = true };
        test.Click += (sender, args) =>
        {
            PlatformTestResult outcome = engine.TestPlatform(urlBox.Text, keyBox.Text);
            result.Text = outcome.Message;
        };
        AddRow(grid, string.Empty, test);
        AddRow(grid, "Diagnostic", result);

        page.Controls.Add(grid);
        return page;
    }

    private static TabPage BuildOptionsTab(
        IntegratorProfileEngine profileEngine,
        Dictionary<string, Func<string?>> valueReaders)
    {
        var page = new TabPage("4. Options");
        var grid = NewGrid();

        AddOptionalTextField(grid, profileEngine, valueReaders, ProfileFieldKeys.Schedule, "Planification (HH:mm, séparées par des virgules)");
        AddOptionalTextField(grid, profileEngine, valueReaders, ProfileFieldKeys.PdfPoolPath, "Dossier du pool de PDF");
        AddOptionalTextField(grid, profileEngine, valueReaders, ProfileFieldKeys.OdbcAdvanced, "Paramètres ODBC avancés");
        AddOptionalTextField(grid, profileEngine, valueReaders, ProfileFieldKeys.Logging, "Journalisation (niveau/rétention)");
        AddOptionalTextField(grid, profileEngine, valueReaders, ProfileFieldKeys.AutoUpdate, "Mise à jour automatique (true/false)");

        page.Controls.Add(grid);
        return page;
    }

    private static TabPage BuildSummaryTab(
        InstallerEngine engine,
        IntegratorProfile profile,
        Dictionary<string, Func<string?>> valueReaders,
        TextBox report)
    {
        var page = new TabPage("5. Résumé");
        var layout = new Panel { Dock = DockStyle.Fill };

        var install = new Button { Text = "Installer l'agent", Dock = DockStyle.Top, Height = 36 };
        install.Click += (sender, args) =>
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Func<string?>> reader in valueReaders)
            {
                values[reader.Key] = reader.Value();
            }

            InstallationResult result = engine.Install(profile, new InstallationInput(values));
            string header = result.Success ? "Installation réussie :" : "Installation NON aboutie :";
            report.Text = header + Environment.NewLine + string.Join(Environment.NewLine, result.Messages);
        };

        layout.Controls.Add(report);
        layout.Controls.Add(install);
        page.Controls.Add(layout);
        return page;
    }

    private static void AddOptionalTextField(
        TableLayoutPanel grid,
        IntegratorProfileEngine profileEngine,
        Dictionary<string, Func<string?>> valueReaders,
        string key,
        string label)
    {
        ResolvedField field = profileEngine.Resolve(key);
        if (!field.IsVisible)
        {
            return;
        }

        var box = new TextBox { Dock = DockStyle.Fill };
        ApplyState(box, field);
        if (field.IsEditable)
        {
            valueReaders[key] = () => box.Text;
        }

        AddRow(grid, label, box);
    }

    private static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(8),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
        return grid;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private static void ApplyState(TextBox box, ResolvedField field)
    {
        if (field.DefaultValue != null)
        {
            box.Text = field.DefaultValue;
        }

        // « verrouillé » : visible mais non éditable (valeur imposée par le profil) ; « affiché » : éditable.
        box.ReadOnly = !field.IsEditable;
    }

    private void BuildUi()
    {
        Text = "Installation de l'agent Liakont";
        Width = 720;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

        var profileEngine = new IntegratorProfileEngine(_profile);
        var valueReaders = new Dictionary<string, Func<string?>>(StringComparer.Ordinal);

        var report = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
        };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildInstanceTab(_engine, profileEngine, valueReaders));
        tabs.TabPages.Add(BuildSourceTab(_engine, _knownAdapters, profileEngine, valueReaders));
        tabs.TabPages.Add(BuildServerTab(_engine, profileEngine, valueReaders));
        tabs.TabPages.Add(BuildOptionsTab(profileEngine, valueReaders));
        tabs.TabPages.Add(BuildSummaryTab(_engine, _profile, valueReaders, report));

        Controls.Add(tabs);
    }
}
