using Jianghu;
using UnityEngine;

public sealed partial class OriginalRig {
    void InitializeStartup() {
        J controllers=LoadOriginalExpressionLibrary()["rigs"][human?PlayerSource:EnemySource];
        // Bind only actual source components. The three fighter prefabs do
        // not contain KienematicDynamicEnabler, so this list is normally empty.
        foreach(var row in controllers["KienematicDynamicEnabler"].A) {
            J data=row["data"];if(!data["m_Enabled"].B)continue;
            var host=ResolveTransform(data["m_GameObject"]);
            host.gameObject.AddComponent<OriginalKienematicDynamicEnabler>().Initialize(this,data);
        }
        foreach(var row in controllers["CharacterAnimationController"].A) {
            J data=row["data"];if(!data["m_Enabled"].B)continue;
            var host=ResolveTransform(data["m_GameObject"]);
            host.gameObject.AddComponent<OriginalAnimationController>().Initialize(Resolve<Animator>(data["animator"]));
        }
    }
}
