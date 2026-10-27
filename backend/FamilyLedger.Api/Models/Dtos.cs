using System;

namespace FamilyLedger.Api.Models
{
    public class FamilyDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string InviteCode { get; set; } = string.Empty;
        public Guid HeadUserId { get; set; }
    }

    public class FamilyMemberDto
    {
        public Guid Id { get; set; }
        public Guid FamilyId { get; set; }
        public Guid UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Relation { get; set; } = string.Empty;
        public bool IsDependent { get; set; }
    }

    public class CategoryDto
    {
        public Guid Id { get; set; }
        public Guid FamilyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }

    public class ExpenseDto
    {
        public Guid Id { get; set; }
        public Guid FamilyId { get; set; }
        public Guid CategoryId { get; set; }
        public Guid MemberUserId { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public bool IsRecurring { get; set; }
        public string ReceiptUrl { get; set; } = string.Empty;
    }

    public class BudgetDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid CategoryId { get; set; }
        public string Month { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SavingsGoalDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal TargetAmount { get; set; }
        public decimal CurrentAmount { get; set; }
        public DateOnly? Deadline { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class EmiDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string LenderName { get; set; } = string.Empty;
        public decimal Principal { get; set; }
        public decimal MonthlyEmi { get; set; }
        public DateOnly StartDate { get; set; }
        public int TenureMonths { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class NotificationDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
