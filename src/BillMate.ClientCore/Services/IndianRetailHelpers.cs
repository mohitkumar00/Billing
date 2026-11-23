using System.Text;

namespace BillMate.ClientCore.Services;

/// <summary>
/// Generates standard NPCI compliant UPI deep-links and QR string formats for Indian payments.
/// </summary>
public static class UpiPaymentHelper
{
    public static string GenerateUpiUri(
        string upiVpa,
        string businessName,
        decimal amount,
        string invoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(upiVpa)) return string.Empty;

        var cleanVpa = upiVpa.Trim();
        var pn = Uri.EscapeDataString(businessName.Trim());
        var tr = Uri.EscapeDataString(invoiceNumber.Trim());
        var tn = Uri.EscapeDataString($"Bill {invoiceNumber.Trim()}");
        var am = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        return $"upi://pay?pa={cleanVpa}&pn={pn}&am={am}&cu=INR&tn={tn}&tr={tr}";
    }
}

/// <summary>
/// Formats WhatsApp deep-links (wa.me) for 1-click digital receipt sharing and Khata (Udhar) reminders.
/// </summary>
public static class WhatsAppShareHelper
{
    public static string BuildInvoiceShareUrl(
        string customerPhone,
        string businessName,
        string invoiceNumber,
        decimal amount,
        decimal amountDue,
        int itemCount,
        string? upiVpa = null)
    {
        var cleanPhone = SanitizePhoneNumber(customerPhone);
        if (string.IsNullOrEmpty(cleanPhone)) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"🧾 *INVOICE FROM {businessName.ToUpperInvariant()}*");
        sb.AppendLine($"Bill No: *#{invoiceNumber}*");
        sb.AppendLine($"Items: {itemCount}");
        sb.AppendLine($"*Total Amount: ₹{amount:N2}*");

        if (amountDue > 0m)
        {
            sb.AppendLine($"⚠️ *Balance Due (Udhar): ₹{amountDue:N2}*");
            if (!string.IsNullOrWhiteSpace(upiVpa))
            {
                var upi = UpiPaymentHelper.GenerateUpiUri(upiVpa, businessName, amountDue, invoiceNumber);
                sb.AppendLine();
                sb.AppendLine($"👉 *Click to Pay via UPI:* {upi}");
            }
        }
        else
        {
            sb.AppendLine("Payment Status: ✅ *PAID IN FULL*");
        }

        sb.AppendLine();
        sb.AppendLine("Thank you for your visit! Have a great day.");

        return $"https://wa.me/{cleanPhone}?text={Uri.EscapeDataString(sb.ToString())}";
    }

    public static string BuildKhataReminderUrl(
        string customerPhone,
        string customerName,
        string businessName,
        decimal outstandingBalance,
        string? upiVpa = null)
    {
        var cleanPhone = SanitizePhoneNumber(customerPhone);
        if (string.IsNullOrEmpty(cleanPhone)) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"Namaste *{customerName}* ji,");
        sb.AppendLine();
        sb.AppendLine($"This is a friendly reminder from *{businessName}* regarding your outstanding account balance (Udhar):");
        sb.AppendLine($"*Pending Amount: ₹{outstandingBalance:N2}*");

        if (!string.IsNullOrWhiteSpace(upiVpa))
        {
            var upi = UpiPaymentHelper.GenerateUpiUri(upiVpa, businessName, outstandingBalance, "KHATA");
            sb.AppendLine();
            sb.AppendLine($"You can pay directly via Google Pay / PhonePe / Paytm here:");
            sb.AppendLine(upi);
        }

        sb.AppendLine();
        sb.AppendLine("Thank you for your continued patronage!");

        return $"https://wa.me/{cleanPhone}?text={Uri.EscapeDataString(sb.ToString())}";
    }

    private static string SanitizePhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) return "91" + digits;
        if (digits.Length == 12 && digits.StartsWith("91")) return digits;
        return digits;
    }
}
