namespace HotelBooking.Api.Models;

public enum AccountStatus { PendingVerification, Active, Locked, Blocked }

public enum BookingStatus { PendingPayment, Paid, Expired, Cancelled }

public enum PaymentMethod { VA, CC, QRIS, EWALLET }

public enum PaymentStatus { Pending, Success, Failed, Expired }

public enum RefundStatus { PendingApproval, Approved, Rejected, Processing, Completed, Failed }

public enum NotificationType { Otp, ETicket, Invoice }

public enum NotificationStatus { Sent, Retrying, Failed }
