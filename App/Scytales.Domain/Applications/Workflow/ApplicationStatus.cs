namespace Scytales.Domain.Applications.Workflow;

public enum ApplicationStatus
{
    Submitted,
    ValidatingData,
    CheckingPhoto,
    RiskAssessment,
    PendingManualReview,
    Approved,
    Rejected,
    Failed
}
