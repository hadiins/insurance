namespace Aqsat.Domain.Enums;

public enum ManualReviewStatus : byte
{
    Pending = 1,
    InReview = 2,
    Approved = 3,
    Rejected = 4,
    RequestMoreInfo = 5,
}
