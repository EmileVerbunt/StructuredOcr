using System.Text.Json;
using StructuredOcr.Models;

namespace StructuredOcr.Services;

/// <summary>
/// Manages predefined and custom schemas. In-memory store — not persisted.
/// </summary>
public class SchemaService
{
    private readonly Dictionary<string, UnifiedSchema> _schemas = new(StringComparer.OrdinalIgnoreCase);

    public SchemaService()
    {
        SeedDefaults();
    }

    public IReadOnlyList<UnifiedSchema> GetAll() => _schemas.Values.ToList();

    /// <summary>
    /// Returns the known document categories derived from schema names,
    /// used by the Classify strategy across all services.
    /// </summary>
    public IReadOnlyList<DocumentCategory> GetDocumentCategories()
    {
        var categories = _schemas.Values.Select(s => new DocumentCategory
        {
            Key = s.Name,
            Description = s.Description ?? s.Name
        }).ToList();

        // Add common catch-all categories not tied to a specific schema
        categories.Add(new DocumentCategory { Key = "Letter", Description = "General correspondence, cover letters, official letters" });
        categories.Add(new DocumentCategory { Key = "Contract", Description = "Legal agreements, service contracts, NDAs" });
        categories.Add(new DocumentCategory { Key = "Report", Description = "Business reports, annual reports, audit reports" });
        categories.Add(new DocumentCategory { Key = "Form", Description = "Application forms, government forms, registration forms" });
        categories.Add(new DocumentCategory { Key = "Receipt", Description = "Payment receipts, transaction confirmations" });
        categories.Add(new DocumentCategory { Key = "Unknown", Description = "Document type could not be determined" });

        return categories;
    }

    public UnifiedSchema? Get(string name) =>
        _schemas.TryGetValue(name, out var s) ? s : null;

    public void AddOrUpdate(UnifiedSchema schema)
    {
        _schemas[schema.Name] = schema;
    }

    public bool Delete(string name) => _schemas.Remove(name);

    private void SeedDefaults()
    {
        AddOrUpdate(new UnifiedSchema
        {
            Name = "Insurance Claim",
            Description = "Extract key data from insurance claim forms and loss reports",
            Fields =
            [
                new SchemaField { Name = "document_type", Type = SchemaFieldType.String, Description = "e.g. First Notice of Loss, Claim Form, Adjuster Report" },
                new SchemaField { Name = "claim_number", Type = SchemaFieldType.String, Description = "Unique claim reference number" },
                new SchemaField { Name = "policy_number", Type = SchemaFieldType.String, Description = "Associated insurance policy number" },
                new SchemaField { Name = "date_of_loss", Type = SchemaFieldType.String, Description = "Date the loss or incident occurred" },
                new SchemaField { Name = "date_filed", Type = SchemaFieldType.String, Description = "Date the claim was submitted" },
                new SchemaField { Name = "claimant", Type = SchemaFieldType.Object, Description = "Person or entity filing the claim",
                    Children = [
                        new SchemaField { Name = "name", Type = SchemaFieldType.String },
                        new SchemaField { Name = "address", Type = SchemaFieldType.String },
                        new SchemaField { Name = "phone", Type = SchemaFieldType.String },
                        new SchemaField { Name = "email", Type = SchemaFieldType.String }
                    ] },
                new SchemaField { Name = "loss_type", Type = SchemaFieldType.String, Description = "e.g. Property Damage, Bodily Injury, Theft, Fire, Water" },
                new SchemaField { Name = "loss_description", Type = SchemaFieldType.String, Description = "Narrative description of the loss event" },
                new SchemaField { Name = "claimed_amount", Type = SchemaFieldType.String, Description = "Total amount claimed (currency + value)" },
                new SchemaField { Name = "line_items", Type = SchemaFieldType.Array, Description = "Individual items or damages claimed",
                    Children = [
                        new SchemaField { Name = "description", Type = SchemaFieldType.String },
                        new SchemaField { Name = "amount", Type = SchemaFieldType.String }
                    ] },
                new SchemaField { Name = "status", Type = SchemaFieldType.String, Description = "Claim status if mentioned (Open, Under Review, Approved, Denied)" },
                new SchemaField { Name = "summary", Type = SchemaFieldType.String, Description = "Brief English summary of the claim" }
            ]
        });

        AddOrUpdate(new UnifiedSchema
        {
            Name = "Invoice",
            Description = "Extract structured data from invoices and billing documents",
            Fields =
            [
                new SchemaField { Name = "document_type", Type = SchemaFieldType.String, Description = "e.g. Invoice, Credit Note, Proforma Invoice" },
                new SchemaField { Name = "invoice_number", Type = SchemaFieldType.String, Description = "Invoice or document reference number" },
                new SchemaField { Name = "invoice_date", Type = SchemaFieldType.String, Description = "Date the invoice was issued" },
                new SchemaField { Name = "due_date", Type = SchemaFieldType.String, Description = "Payment due date" },
                new SchemaField { Name = "currency", Type = SchemaFieldType.String, Description = "Currency code (e.g. USD, EUR, GBP)" },
                new SchemaField { Name = "vendor", Type = SchemaFieldType.Object, Description = "Seller / issuing party",
                    Children = [
                        new SchemaField { Name = "name", Type = SchemaFieldType.String },
                        new SchemaField { Name = "address", Type = SchemaFieldType.String },
                        new SchemaField { Name = "tax_id", Type = SchemaFieldType.String, Description = "VAT / Tax ID number" }
                    ] },
                new SchemaField { Name = "buyer", Type = SchemaFieldType.Object, Description = "Purchaser / billing party",
                    Children = [
                        new SchemaField { Name = "name", Type = SchemaFieldType.String },
                        new SchemaField { Name = "address", Type = SchemaFieldType.String },
                        new SchemaField { Name = "tax_id", Type = SchemaFieldType.String, Description = "VAT / Tax ID number" }
                    ] },
                new SchemaField { Name = "line_items", Type = SchemaFieldType.Array, Description = "Individual billed items or services",
                    Children = [
                        new SchemaField { Name = "description", Type = SchemaFieldType.String },
                        new SchemaField { Name = "quantity", Type = SchemaFieldType.Number },
                        new SchemaField { Name = "unit_price", Type = SchemaFieldType.String },
                        new SchemaField { Name = "amount", Type = SchemaFieldType.String }
                    ] },
                new SchemaField { Name = "subtotal", Type = SchemaFieldType.String, Description = "Subtotal before tax" },
                new SchemaField { Name = "tax_amount", Type = SchemaFieldType.String, Description = "Total tax amount" },
                new SchemaField { Name = "total", Type = SchemaFieldType.String, Description = "Grand total including tax" },
                new SchemaField { Name = "payment_terms", Type = SchemaFieldType.String, Description = "Payment terms or instructions" },
                new SchemaField { Name = "summary", Type = SchemaFieldType.String, Description = "Brief English summary of the invoice" }
            ]
        });

        AddOrUpdate(new UnifiedSchema
        {
            Name = "Balance Sheet",
            Description = "Extract financial position data from balance sheets and financial statements",
            Fields =
            [
                new SchemaField { Name = "document_type", Type = SchemaFieldType.String, Description = "e.g. Balance Sheet, Statement of Financial Position" },
                new SchemaField { Name = "entity_name", Type = SchemaFieldType.String, Description = "Company or reporting entity name" },
                new SchemaField { Name = "reporting_date", Type = SchemaFieldType.String, Description = "As-of date for the statement" },
                new SchemaField { Name = "reporting_period", Type = SchemaFieldType.String, Description = "e.g. Q4 2025, FY 2025, Year ended Dec 31 2025" },
                new SchemaField { Name = "currency", Type = SchemaFieldType.String, Description = "Reporting currency (e.g. USD, EUR)" },
                new SchemaField { Name = "assets", Type = SchemaFieldType.Object, Description = "Total assets breakdown",
                    Children = [
                        new SchemaField { Name = "current_assets", Type = SchemaFieldType.Object, Description = "Short-term assets (within 12 months)",
                            Children = [
                                new SchemaField { Name = "cash_and_cash_equivalents", Type = SchemaFieldType.String, Description = "Cash, bank balances, money market funds" },
                                new SchemaField { Name = "short_term_investments", Type = SchemaFieldType.String, Description = "Marketable securities, treasury bills" },
                                new SchemaField { Name = "accounts_receivable_gross", Type = SchemaFieldType.String, Description = "Total trade receivables before allowance" },
                                new SchemaField { Name = "allowance_for_doubtful_accounts", Type = SchemaFieldType.String, Description = "Provision for uncollectible receivables" },
                                new SchemaField { Name = "accounts_receivable_net", Type = SchemaFieldType.String, Description = "Net trade receivables after allowance" },
                                new SchemaField { Name = "inventory", Type = SchemaFieldType.Object, Description = "Inventory breakdown",
                                    Children = [
                                        new SchemaField { Name = "raw_materials", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "work_in_progress", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "finished_goods", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "total_inventory", Type = SchemaFieldType.String }
                                    ] },
                                new SchemaField { Name = "prepaid_expenses", Type = SchemaFieldType.String, Description = "Rent, insurance, other prepayments" },
                                new SchemaField { Name = "other_current_assets", Type = SchemaFieldType.Array, Description = "Any other current assets",
                                    Children = [
                                        new SchemaField { Name = "name", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "amount", Type = SchemaFieldType.String }
                                    ] },
                                new SchemaField { Name = "total_current_assets", Type = SchemaFieldType.String }
                            ] },
                        new SchemaField { Name = "non_current_assets", Type = SchemaFieldType.Object, Description = "Long-term assets (beyond 12 months)",
                            Children = [
                                new SchemaField { Name = "property_plant_equipment_gross", Type = SchemaFieldType.String, Description = "PP&E at cost / gross value" },
                                new SchemaField { Name = "accumulated_depreciation", Type = SchemaFieldType.String, Description = "Total accumulated depreciation on PP&E" },
                                new SchemaField { Name = "property_plant_equipment_net", Type = SchemaFieldType.String, Description = "PP&E net of depreciation" },
                                new SchemaField { Name = "intangible_assets", Type = SchemaFieldType.Object, Description = "Non-physical long-term assets",
                                    Children = [
                                        new SchemaField { Name = "goodwill", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "patents_and_trademarks", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "software_and_licenses", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "accumulated_amortization", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "total_intangible_assets", Type = SchemaFieldType.String }
                                    ] },
                                new SchemaField { Name = "long_term_investments", Type = SchemaFieldType.String, Description = "Equity investments, bonds held to maturity" },
                                new SchemaField { Name = "deferred_tax_assets", Type = SchemaFieldType.String },
                                new SchemaField { Name = "right_of_use_assets", Type = SchemaFieldType.String, Description = "Operating and finance lease assets" },
                                new SchemaField { Name = "other_non_current_assets", Type = SchemaFieldType.Array, Description = "Any other non-current assets",
                                    Children = [
                                        new SchemaField { Name = "name", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "amount", Type = SchemaFieldType.String }
                                    ] },
                                new SchemaField { Name = "total_non_current_assets", Type = SchemaFieldType.String }
                            ] },
                        new SchemaField { Name = "total_assets", Type = SchemaFieldType.String }
                    ] },
                new SchemaField { Name = "liabilities", Type = SchemaFieldType.Object, Description = "Total liabilities breakdown",
                    Children = [
                        new SchemaField { Name = "current_liabilities", Type = SchemaFieldType.Object, Description = "Obligations due within 12 months",
                            Children = [
                                new SchemaField { Name = "accounts_payable", Type = SchemaFieldType.String, Description = "Trade payables to suppliers" },
                                new SchemaField { Name = "accrued_expenses", Type = SchemaFieldType.String, Description = "Wages, interest, taxes accrued but not yet paid" },
                                new SchemaField { Name = "short_term_debt", Type = SchemaFieldType.String, Description = "Bank overdrafts, lines of credit, current portion of long-term debt" },
                                new SchemaField { Name = "current_portion_of_long_term_debt", Type = SchemaFieldType.String, Description = "Long-term debt maturing within 12 months" },
                                new SchemaField { Name = "deferred_revenue", Type = SchemaFieldType.String, Description = "Unearned revenue / customer deposits" },
                                new SchemaField { Name = "income_tax_payable", Type = SchemaFieldType.String },
                                new SchemaField { Name = "current_lease_liabilities", Type = SchemaFieldType.String, Description = "Lease obligations due within 12 months" },
                                new SchemaField { Name = "other_current_liabilities", Type = SchemaFieldType.Array, Description = "Any other current liabilities",
                                    Children = [
                                        new SchemaField { Name = "name", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "amount", Type = SchemaFieldType.String }
                                    ] },
                                new SchemaField { Name = "total_current_liabilities", Type = SchemaFieldType.String }
                            ] },
                        new SchemaField { Name = "non_current_liabilities", Type = SchemaFieldType.Object, Description = "Obligations due beyond 12 months",
                            Children = [
                                new SchemaField { Name = "long_term_debt", Type = SchemaFieldType.String, Description = "Bonds, mortgages, term loans (excluding current portion)" },
                                new SchemaField { Name = "deferred_tax_liabilities", Type = SchemaFieldType.String },
                                new SchemaField { Name = "pension_and_retirement_obligations", Type = SchemaFieldType.String },
                                new SchemaField { Name = "non_current_lease_liabilities", Type = SchemaFieldType.String, Description = "Long-term lease obligations" },
                                new SchemaField { Name = "provisions", Type = SchemaFieldType.String, Description = "Warranty, legal, restructuring provisions" },
                                new SchemaField { Name = "other_non_current_liabilities", Type = SchemaFieldType.Array, Description = "Any other non-current liabilities",
                                    Children = [
                                        new SchemaField { Name = "name", Type = SchemaFieldType.String },
                                        new SchemaField { Name = "amount", Type = SchemaFieldType.String }
                                    ] },
                                new SchemaField { Name = "total_non_current_liabilities", Type = SchemaFieldType.String }
                            ] },
                        new SchemaField { Name = "total_liabilities", Type = SchemaFieldType.String }
                    ] },
                new SchemaField { Name = "equity", Type = SchemaFieldType.Object, Description = "Shareholders equity breakdown",
                    Children = [
                        new SchemaField { Name = "common_stock", Type = SchemaFieldType.String, Description = "Par value of issued common shares" },
                        new SchemaField { Name = "preferred_stock", Type = SchemaFieldType.String, Description = "Par value of issued preferred shares" },
                        new SchemaField { Name = "additional_paid_in_capital", Type = SchemaFieldType.String, Description = "Share premium / capital surplus" },
                        new SchemaField { Name = "retained_earnings", Type = SchemaFieldType.String },
                        new SchemaField { Name = "treasury_stock", Type = SchemaFieldType.String, Description = "Cost of repurchased shares (contra equity)" },
                        new SchemaField { Name = "accumulated_other_comprehensive_income", Type = SchemaFieldType.String, Description = "Unrealized gains/losses, foreign currency adjustments" },
                        new SchemaField { Name = "minority_interest", Type = SchemaFieldType.String, Description = "Non-controlling interest in subsidiaries" },
                        new SchemaField { Name = "total_equity", Type = SchemaFieldType.String }
                    ] },
                new SchemaField { Name = "total_liabilities_and_equity", Type = SchemaFieldType.String, Description = "Must equal total assets" },
                new SchemaField { Name = "notes", Type = SchemaFieldType.Array, Description = "Key footnotes, accounting policies, or contingencies referenced on the statement",
                    Children = [
                        new SchemaField { Name = "note", Type = SchemaFieldType.String }
                    ] },
                new SchemaField { Name = "summary", Type = SchemaFieldType.String, Description = "Brief English summary of the financial position" }
            ]
        });
    }
}
