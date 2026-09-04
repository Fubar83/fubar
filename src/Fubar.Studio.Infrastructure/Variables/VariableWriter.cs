using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Infrastructure.Variables;

/// <inheritdoc cref="IVariableWriter"/>
public sealed class VariableWriter : IVariableWriter
{
    private readonly ISecretStoreService _secretStore;
    private readonly ISessionVariableStore _sessionStore;

    public VariableWriter(ISecretStoreService secretStore, ISessionVariableStore sessionStore)
    {
        _secretStore = secretStore;
        _sessionStore = sessionStore;
    }

    public VariableWriteResult Write(
        Workspace workspace,
        WorkspaceEnvironment environment,
        string key,
        string? value,
        VariableKind requestedKind = VariableKind.Normal)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var existing = environment.Variables.FirstOrDefault(v => v.Key == key);

        // An existing variable keeps its kind. This is the whole fix: a capture naming a Secret
        // variable must not be able to turn it into a Normal one and write the value to disk.
        var kind = existing?.Kind ?? requestedKind;

        switch (kind)
        {
            case VariableKind.Secret:
                _secretStore.SetSecret(workspace.WorkspaceId, key, value ?? "");
                break;

            case VariableKind.Session:
                _sessionStore.Set(SessionScope.For(workspace, environment.Id), key, value);
                break;
        }

        if (existing is not null)
        {
            // Only a Normal variable carries its value on disk; for the other two the file entry is a
            // declaration whose Value must stay null (see AppVariable).
            var newValue = kind == VariableKind.Normal ? value ?? "" : null;
            if (existing.Value == newValue)
            {
                return new VariableWriteResult(kind, EnvironmentChanged: false);
            }

            existing.Value = newValue;
            return new VariableWriteResult(kind, EnvironmentChanged: true);
        }

        environment.Variables.Add(new AppVariable
        {
            Key = key,
            Value = kind == VariableKind.Normal ? value ?? "" : null,
            Kind = kind,
        });

        return new VariableWriteResult(kind, EnvironmentChanged: true);
    }
}
