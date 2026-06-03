using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.JCMU.SDK.Models;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.JinnDocs;

public class JinnDocsAddon : IJcmuAddon
{
    public async Task<Maybe<int>> ExecuteAsync(ActionContext context)
    {
        return Maybe.Some(-1);
    }
}