using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BillMate.ClientCore.Services;

/// <summary>
/// Provides real-time dynamic UI localization between English and Hindi (and other Indic languages)
/// without requiring application restarts.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    private string _currentLanguage = "en"; // "en" or "hi"

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged(string.Empty); // Notifies all indexers and properties
            }
        }
    }

    public bool IsHindi => _currentLanguage == "hi";

    public string this[string key] => Get(key);

    public string Get(string key, string? defaultText = null)
    {
        if (_strings.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var val))
            return val;

        if (_strings["en"].TryGetValue(key, out var enVal))
            return enVal;

        return defaultText ?? key;
    }

    public void ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage == "en" ? "hi" : "en";
    }

    private static readonly Dictionary<string, Dictionary<string, string>> _strings = new()
    {
        ["en"] = new()
        {
            ["Nav_Pos"] = "POS Counter",
            ["Nav_Invoices"] = "Invoices",
            ["Nav_Khata"] = "Khata (Credit)",
            ["Nav_Reports"] = "GST Reports",
            ["Nav_Dashboard"] = "Dashboard",
            ["Nav_Logout"] = "Switch Cashier",

            ["Pos_SearchPlaceholder"] = "Search products by name or scan barcode...",
            ["Pos_CustomerPlaceholder"] = "Customer Name or Phone (Optional)",
            ["Pos_Hold"] = "Hold Cart",
            ["Pos_Clear"] = "Clear Cart",
            ["Pos_Subtotal"] = "Subtotal",
            ["Pos_Tax"] = "GST Tax",
            ["Pos_Discount"] = "Discount",
            ["Pos_GrandTotal"] = "GRAND TOTAL",
            ["Pos_CompleteSale"] = "Complete Sale (F12)",
            ["Pos_PayCash"] = "Cash",
            ["Pos_PayUpi"] = "UPI / QR",
            ["Pos_PayCard"] = "Card",
            ["Pos_PayCredit"] = "Udhar (Credit)",
            ["Pos_ShareWhatsApp"] = "Share Bill on WhatsApp",
            ["Pos_PrintReceipt"] = "Print Receipt",
            ["Pos_NewBill"] = "New Bill",

            ["Khata_Title"] = "Customer Khata & Udhar Ledger",
            ["Khata_Search"] = "Search customer by name or phone...",
            ["Khata_TotalOutstanding"] = "Total Outstanding (Udhar)",
            ["Khata_CustomersWithDue"] = "Customers with Due",
            ["Khata_ReceivePayment"] = "Receive Payment",
            ["Khata_SendReminder"] = "WhatsApp Reminder",
            ["Khata_BalanceDue"] = "Balance Due",
            ["Khata_CreditLimit"] = "Credit Limit",

            ["Inv_Title"] = "Invoice History",
            ["Inv_Search"] = "Search by invoice # or customer...",
            ["Inv_WhatsApp"] = "WhatsApp",
            ["Inv_Print"] = "Print",
            ["Inv_Cancel"] = "Cancel"
        },
        ["hi"] = new()
        {
            ["Nav_Pos"] = "पीओएस (नया बिल)",
            ["Nav_Invoices"] = "रसीदें",
            ["Nav_Khata"] = "खाता बही (उधार)",
            ["Nav_Reports"] = "जीएसटी रिपोर्ट",
            ["Nav_Dashboard"] = "डैशबोर्ड",
            ["Nav_Logout"] = "कैशियर बदलें",

            ["Pos_SearchPlaceholder"] = "उत्पाद का नाम या बारकोड स्कैन करें...",
            ["Pos_CustomerPlaceholder"] = "ग्राहक का नाम या मोबाइल नंबर...",
            ["Pos_Hold"] = "बिल रोकें",
            ["Pos_Clear"] = "खाली करें",
            ["Pos_Subtotal"] = "उप-योग",
            ["Pos_Tax"] = "जीएसटी टैक्स",
            ["Pos_Discount"] = "छूट",
            ["Pos_GrandTotal"] = "कुल देय राशि",
            ["Pos_CompleteSale"] = "बिल पूर्ण करें (F12)",
            ["Pos_PayCash"] = "नकद (Cash)",
            ["Pos_PayUpi"] = "यूपीआई / क्यूआर",
            ["Pos_PayCard"] = "कार्ड",
            ["Pos_PayCredit"] = "उधार खाता",
            ["Pos_ShareWhatsApp"] = "व्हाट्सएप पर बिल भेजें",
            ["Pos_PrintReceipt"] = "रसीद प्रिंट करें",
            ["Pos_NewBill"] = "नया बिल बनाएं",

            ["Khata_Title"] = "ग्राहक खाता व उधार बही",
            ["Khata_Search"] = "ग्राहक का नाम या फोन नंबर खोजें...",
            ["Khata_TotalOutstanding"] = "कुल बकाया उधार",
            ["Khata_CustomersWithDue"] = "उधार वाले ग्राहक",
            ["Khata_ReceivePayment"] = "जमा राशि दर्ज करें",
            ["Khata_SendReminder"] = "व्हाट्सएप तकाजा भेजें",
            ["Khata_BalanceDue"] = "बकाया राशि",
            ["Khata_CreditLimit"] = "उधार सीमा",

            ["Inv_Title"] = "बिक्री रसीद इतिहास",
            ["Inv_Search"] = "बिल नंबर या ग्राहक खोजें...",
            ["Inv_WhatsApp"] = "व्हाट्सएप",
            ["Inv_Print"] = "प्रिंट",
            ["Inv_Cancel"] = "रद्द करें"
        }
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
