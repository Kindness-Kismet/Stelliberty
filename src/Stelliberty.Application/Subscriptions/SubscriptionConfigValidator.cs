using Stelliberty.Domain.Subscriptions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionConfigValidator
{
    public void Validate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Configuration file is empty");
        }

        var root = LoadRoot(content);
        if (!root.Children.ContainsKey(new YamlScalarNode("proxies")) && !root.Children.ContainsKey(new YamlScalarNode("proxy-groups")))
        {
            throw new InvalidOperationException("Configuration file format is invalid: missing proxies or proxy-groups");
        }

        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('\t'))
            {
                throw new InvalidOperationException($"Configuration file format error: line {i + 1} uses tab indentation");
            }
        }
    }

    private static YamlMappingNode LoadRoot(string content)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(content));
            return stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode root
                ? root
                : throw new InvalidOperationException("Configuration file YAML root must be a mapping");
        }
        catch (YamlException exception)
        {
            throw new InvalidOperationException("Configuration file YAML format is invalid", exception);
        }
    }
}
