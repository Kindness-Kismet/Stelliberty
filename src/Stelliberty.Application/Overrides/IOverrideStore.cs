using Stelliberty.Domain.Overrides;
namespace Stelliberty.Application.Overrides;

public interface IOverrideStore
{
    void Save(OverrideProfile overrideProfile, string content);

    IReadOnlyList<OverrideProfile> LoadOverrides();

    string ReadContent(string overrideId);

    string GetContentPath(string overrideId);

    void SaveOverrides(IReadOnlyList<OverrideProfile> overrides);

    void Delete(string overrideId);
}
