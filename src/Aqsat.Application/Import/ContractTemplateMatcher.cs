using Aqsat.Domain;

namespace Aqsat.Application.Import;

/// <summary>
/// The 80% problem (docs/PHASE-1-SPEC.md): most installment policies are identified by the
/// counterparty contract name, not the literal «اقساطی» label. Matching is contains-on-the-raw-
/// contract-column against each agency-configured ContractTemplate.ContractNamePattern — never a
/// hardcoded search for «اقساطی» itself, which would silently drop ~80% of real installment
/// policies (CLAUDE.md, docs/TASKS.md Task 7's check).
/// </summary>
public static class ContractTemplateMatcher
{
    public static ContractTemplate? Match(string contractName, IEnumerable<ContractTemplate> templates)
    {
        if (string.IsNullOrWhiteSpace(contractName))
        {
            return null;
        }

        return templates.FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.ContractNamePattern) &&
            contractName.Contains(t.ContractNamePattern, StringComparison.Ordinal));
    }
}
