using Jianghu;
using UnityEngine;

public sealed partial class OriginalRig {
    public OriginalExpressions OriginalExpression {get;private set;}
    static J originalExpressionLibrary;
    static J LoadOriginalExpressionLibrary() {
        if(originalExpressionLibrary==null)originalExpressionLibrary=J.Parse(Resources.Load<TextAsset>("OriginalExpressionSource").text);
        return originalExpressionLibrary;
    }
    void InitializeExpressions() {
        LoadOriginalExpressionLibrary();
        J controllers=originalExpressionLibrary["rigs"][human?PlayerSource:EnemySource];
        J expression=controllers["CharacterExpressions"][0]["data"],voiceData=controllers["VoiceSFX"][0]["data"];
        var voiceTransform=ResolveTransform(voiceData["m_GameObject"]);
        var voice=voiceTransform.gameObject.AddComponent<OriginalVoice>();voice.Initialize(this,voiceData,originalExpressionLibrary,AppearanceConfig);
        var host=ResolveTransform(expression["m_GameObject"]);
        var animator=Resolve<Animator>(controllers["AnimancerComponent"][0]["data"]["_Animator"]);
        OriginalExpression=host.gameObject.AddComponent<OriginalExpressions>();OriginalExpression.Initialize(this,expression,originalExpressionLibrary,animator,voice);
        OriginalAIChargeRequested+=OriginalExpression.Charge;
        PassedOut+=OnExpressionPassedOut;Recovered+=OnExpressionRecovered;
        // CharacterVitality.Spawned -> cmv -> CharacterExpressions.Idle.
        OriginalExpression.Idle();
    }
    void OnExpressionPassedOut(OriginalRig rig){OriginalExpression.PantHeavy();}
    void OnExpressionRecovered(OriginalRig rig){OriginalExpression.Idle();}
}
