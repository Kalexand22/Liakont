namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Stages in the action pipeline, executed in numeric order.
/// </summary>
public enum ActionStage
{
    PreValidation = 10,
    PreOperation = 20,
    MainOperation = 30,
    PostOperation = 40,
}
