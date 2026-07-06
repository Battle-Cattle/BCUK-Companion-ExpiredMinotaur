using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

public sealed class WizConfigStore(string dataFolderName, EventActionTypeRegistry registry)
    : EventActionConfigStore<WizConfig>(dataFolderName, "wiz-config.json", registry);
