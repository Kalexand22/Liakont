namespace Stratum.Common.UI.Components;

using Microsoft.AspNetCore.Components;
using Stratum.Common.UI.Models;

/// <summary>
/// Code-behind for FormBuilderCanvas (UIE02).
/// Manages sections/fields state, undo/redo, selection, and template round-trip.
/// </summary>
public partial class FormBuilderCanvas
{
    /// <summary>Field type labels used by child components.</summary>
    internal static readonly Dictionary<string, string> FieldTypeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = "Texte court",
        ["text_area"] = "Zone de texte",
        ["number"] = "Nombre",
        ["date"] = "Date",
        ["datetime"] = "Date et heure",
        ["time"] = "Heure",
        ["duration"] = "Durée",
        ["boolean"] = "Case à cocher",
        ["single_select"] = "Liste déroulante",
        ["multi_select"] = "Sélection multiple",
        ["email"] = "Email",
        ["phone"] = "Téléphone",
        ["url"] = "URL",
        ["file_upload"] = "Téléchargement",
        ["geo_draw"] = "Périmètre carte",
        ["geo_select"] = "Cadastre",
        ["currency"] = "Montant",
        ["rating"] = "Notation",
    };

    // ── Undo/redo stacks ──
    private readonly Stack<FormBuilderSnapshot> _undoStack = new();

    private readonly Stack<FormBuilderSnapshot> _redoStack = new();

    // ── Internal state ──
    private FormBuilderTemplateState _template = new();

    private List<FormBuilderSectionState> _sections = [];

    private int _selectedSectionIdx = -1;

    private int _selectedFieldIdx = -1;

    private bool _showPreview;

    private FormBuilderTemplateState? _previousValue;

    /// <summary>Initial template to load. Null creates a blank template.</summary>
    [Parameter]
    public FormBuilderTemplateState? Value { get; set; }

    /// <summary>Raised when the user clicks Save. Receives the current template state.</summary>
    [Parameter]
    public EventCallback<FormBuilderTemplateState> OnSave { get; set; }

    /// <summary>
    /// Optional render fragment for preview mode. Receives the current template state.
    /// The consumer uses this to render StratumDynamicForm or any other preview.
    /// </summary>
    [Parameter]
    public RenderFragment<FormBuilderTemplateState>? PreviewContent { get; set; }

    /// <summary>Optional test identifier prefix.</summary>
    [Parameter]
    public string? TestId { get; set; }

    private bool CanUndo => _undoStack.Count > 0;

    private bool CanRedo => _redoStack.Count > 0;

    /// <summary>Build a snapshot of the current template state (deep clone).</summary>
    internal FormBuilderTemplateState GetTemplateState()
    {
        return CloneTemplate();
    }

    // ── Lifecycle ──
    protected override void OnParametersSet()
    {
        if (Value is not null && !ReferenceEquals(Value, _previousValue))
        {
            LoadFromState(Value);
            _previousValue = Value;
        }
        else if (Value is null && _sections.Count == 0)
        {
            _template = new FormBuilderTemplateState
            {
                Name = "Nouveau formulaire",
                Code = "new_form",
                Version = 1,
            };
        }
    }

    // ── Test ID helpers ──
    private string TidBase() => TestId ?? "form-builder";

    private string Tid(string suffix) => $"{TidBase()}-{suffix}";

    // ── State round-trip ──
    private void LoadFromState(FormBuilderTemplateState state)
    {
        var clone = new FormBuilderTemplateState
        {
            Id = state.Id,
            Code = state.Code,
            Name = state.Name,
            Version = state.Version,
            Description = state.Description,
            EntityType = state.EntityType,
        };
        clone.Sections = state.Sections.Select(s => new FormBuilderSectionState
        {
            Id = s.Id,
            Code = s.Code,
            Title = s.Title,
            SortOrder = s.SortOrder,
            VisibilityConditionJson = s.VisibilityConditionJson,
            Fields = s.Fields.Select(f => new FormBuilderFieldState
            {
                Id = f.Id,
                Code = f.Code,
                FieldType = f.FieldType,
                Label = f.Label,
                HelpText = f.HelpText,
                Placeholder = f.Placeholder,
                SortOrder = f.SortOrder,
                IsRequired = f.IsRequired,
                DefaultValueJson = f.DefaultValueJson,
                ValidationRules = f.ValidationRules.Select(r => new FormBuilderValidationRule
                {
                    Type = r.Type,
                    ParamsJson = r.ParamsJson,
                    ErrorMessage = r.ErrorMessage,
                }).ToList(),
                VisibilityConditionJson = f.VisibilityConditionJson,
                OptionsJson = f.OptionsJson,
            }).ToList(),
        }).ToList();
        _template = clone;
        _sections = clone.Sections;
    }

    // ── Undo / Redo ──
    private void PushUndo()
    {
        _undoStack.Push(TakeSnapshot());
        _redoStack.Clear();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(TakeSnapshot());
        RestoreSnapshot(_undoStack.Pop());
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(TakeSnapshot());
        RestoreSnapshot(_redoStack.Pop());
    }

    private FormBuilderSnapshot TakeSnapshot()
    {
        return new FormBuilderSnapshot(CloneTemplate(), _selectedSectionIdx, _selectedFieldIdx);
    }

    private void RestoreSnapshot(FormBuilderSnapshot snapshot)
    {
        _template = snapshot.Template;
        _sections = _template.Sections;
        _selectedSectionIdx = snapshot.SelectedSectionIdx;
        _selectedFieldIdx = snapshot.SelectedFieldIdx;
    }

    private FormBuilderTemplateState CloneTemplate()
    {
        return new FormBuilderTemplateState
        {
            Id = _template.Id,
            Code = _template.Code,
            Name = _template.Name,
            Version = _template.Version,
            Description = _template.Description,
            EntityType = _template.EntityType,
            Sections = _sections.Select(s => new FormBuilderSectionState
            {
                Id = s.Id,
                Code = s.Code,
                Title = s.Title,
                SortOrder = s.SortOrder,
                VisibilityConditionJson = s.VisibilityConditionJson,
                Fields = s.Fields.Select(f => new FormBuilderFieldState
                {
                    Id = f.Id,
                    Code = f.Code,
                    FieldType = f.FieldType,
                    Label = f.Label,
                    HelpText = f.HelpText,
                    Placeholder = f.Placeholder,
                    SortOrder = f.SortOrder,
                    IsRequired = f.IsRequired,
                    DefaultValueJson = f.DefaultValueJson,
                    ValidationRules = f.ValidationRules.Select(r => new FormBuilderValidationRule
                    {
                        Type = r.Type,
                        ParamsJson = r.ParamsJson,
                        ErrorMessage = r.ErrorMessage,
                    }).ToList(),
                    VisibilityConditionJson = f.VisibilityConditionJson,
                    OptionsJson = f.OptionsJson,
                }).ToList(),
            }).ToList(),
        };
    }

    // ── Selection ──
    private void SelectSection(int sectionIdx)
    {
        _selectedSectionIdx = sectionIdx;
        _selectedFieldIdx = -1;
    }

    private void SelectField(int sectionIdx, int fieldIdx)
    {
        _selectedSectionIdx = sectionIdx;
        _selectedFieldIdx = fieldIdx;
    }

    // ── Section operations ──
    private void AddSection()
    {
        PushUndo();
        AddSectionInternal();
    }

    private void AddSectionInternal()
    {
        var idx = _sections.Count;
        _sections.Add(new FormBuilderSectionState
        {
            Code = $"section_{idx + 1}",
            Title = $"Section {idx + 1}",
            SortOrder = idx,
        });
        _selectedSectionIdx = idx;
        _selectedFieldIdx = -1;
    }

    // ── Field operations ──
    private void HandlePaletteFieldSelected(string fieldType)
    {
        PushUndo();

        if (_sections.Count == 0)
        {
            AddSectionInternal();
        }

        var targetSection = _selectedSectionIdx >= 0 && _selectedSectionIdx < _sections.Count
            ? _selectedSectionIdx
            : _sections.Count - 1;

        var section = _sections[targetSection];
        var fieldIdx = section.Fields.Count;
        var label = FieldTypeLabels.TryGetValue(fieldType, out var l) ? l : fieldType;

        section.Fields.Add(new FormBuilderFieldState
        {
            Code = $"{fieldType}_{fieldIdx + 1}",
            FieldType = fieldType,
            Label = $"{label} {fieldIdx + 1}",
            SortOrder = fieldIdx,
        });

        _selectedSectionIdx = targetSection;
        _selectedFieldIdx = fieldIdx;
    }

    private void MoveFieldUp(int sectionIdx, int fieldIdx)
    {
        if (fieldIdx <= 0)
        {
            return;
        }

        PushUndo();
        var fields = _sections[sectionIdx].Fields;
        (fields[fieldIdx], fields[fieldIdx - 1]) = (fields[fieldIdx - 1], fields[fieldIdx]);

        if (_selectedSectionIdx == sectionIdx && _selectedFieldIdx == fieldIdx)
        {
            _selectedFieldIdx = fieldIdx - 1;
        }
    }

    private void MoveFieldDown(int sectionIdx, int fieldIdx)
    {
        var fields = _sections[sectionIdx].Fields;
        if (fieldIdx >= fields.Count - 1)
        {
            return;
        }

        PushUndo();
        (fields[fieldIdx], fields[fieldIdx + 1]) = (fields[fieldIdx + 1], fields[fieldIdx]);

        if (_selectedSectionIdx == sectionIdx && _selectedFieldIdx == fieldIdx)
        {
            _selectedFieldIdx = fieldIdx + 1;
        }
    }

    private void RemoveField(int sectionIdx, int fieldIdx)
    {
        PushUndo();
        _sections[sectionIdx].Fields.RemoveAt(fieldIdx);

        if (_selectedSectionIdx == sectionIdx && _selectedFieldIdx >= fieldIdx)
        {
            _selectedFieldIdx = Math.Min(_selectedFieldIdx - 1, _sections[sectionIdx].Fields.Count - 1);
            if (_selectedFieldIdx < 0)
            {
                _selectedFieldIdx = -1;
            }
        }
    }

    // ── Property changes ──
    private void HandleFieldPropertyChanged(FormBuilderFieldState updated)
    {
        if (_selectedSectionIdx < 0 || _selectedFieldIdx < 0)
        {
            return;
        }

        PushUndo();
        _sections[_selectedSectionIdx].Fields[_selectedFieldIdx] = updated;
    }

    private void HandleSectionPropertyChanged(FormBuilderSectionState updated)
    {
        if (_selectedSectionIdx < 0)
        {
            return;
        }

        PushUndo();
        var fields = _sections[_selectedSectionIdx].Fields;
        updated.Fields = fields;
        _sections[_selectedSectionIdx] = updated;
    }

    // ── Preview / Save ──
    private void TogglePreview() => _showPreview = !_showPreview;

    private async Task HandleSave()
    {
        await OnSave.InvokeAsync(GetTemplateState());
    }

    // ── State records ──
    private sealed record FormBuilderSnapshot(
        FormBuilderTemplateState Template,
        int SelectedSectionIdx,
        int SelectedFieldIdx);
}
