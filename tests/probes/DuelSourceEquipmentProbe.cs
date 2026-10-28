using System;
using System.Collections.Generic;
using System.IO;
using Jianghu;
using UnityEngine;

public static class EquipmentProbe {
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static void Near(float a,float b,string label)=>Check(Math.Abs(a-b)<.0001f,label+" "+a+" != "+b);
    static DuelWeapon Weapon(string profile,string template="",float mass=-1,float damage=-1,string name=""){
        DuelWeapon basis=DuelWeapon.Fist;
        foreach(var x in DuelWeapon.All)if(x.id==profile)basis=x;
        return DuelWeapon.CampaignWeapon(J.O("id",profile,"template_id",template,"name",name,"mass",mass<0?basis.mass:mass,"damage",damage<0?basis.damage:damage,"handling",basis.handling,"cost",basis.cost,"pressure",basis.pressure,"distance",basis.distance));
    }
    public static void Main(string[] args){
        Resources.root=args[0];
        var mapping=J.Parse(File.ReadAllText(Path.Combine(Resources.root,"equipment.json")));
        foreach(var row in mapping["weapons"].D){
            var profile=row.Value["profile"].S;
            var spec=Weapon(profile,row.Key,damage:(float)row.Value["damage_coefficient"].N);
            Near((1+DuelSourceEquipment.DamageStat(spec)/15)/(1+1f/15),spec.damage,row.Key+" ratio");
            Check(DuelSourceEquipment.Surface(spec).ToString()==row.Value["head"].S,row.Key+" head");
            Check(DuelSourceEquipment.Surface(spec,true).ToString()==row.Value["handle"].S,row.Key+" handle");
        }
        Check(DuelSourceEquipment.Surface(Weapon("sword","wood_sword"))==DuelReferenceRules.Surface.WoodBlunt,"wood identity");
        Check(DuelSourceEquipment.Surface(Weapon("sword","iron_practice_sword"))==DuelReferenceRules.Surface.MetalBlunt,"unsharpened iron");
        Check(DuelSourceEquipment.Surface(Weapon("sword","iron_sword",damage:.8f))==DuelReferenceRules.Surface.MetalBlade,"low coefficient is not wood");
        Check(DuelSourceEquipment.Surface(Weapon("sword",name:"枣木剑"))==DuelReferenceRules.Surface.WoodBlunt,"legacy exact name");
        Check(DuelSourceEquipment.Surface(Weapon("sword","iron_sword",name:"枣木剑"))==DuelReferenceRules.Surface.MetalBlade,"ID beats name");
        Near(DuelSourceEquipment.SpeedThreshold(Weapon("sword","wood_sword"),true),10,"blunt handle threshold");
        Near(DuelSourceEquipment.SpeedThreshold(Weapon("sword","iron_sword"),true),7.5f,"blade handle threshold");
        Near(DuelSourceEquipment.SpeedThreshold(Weapon("mace","spiked_mace")),20,"mace blunt threshold");
        Near(DuelSourceEquipment.DamageStat(DuelWeapon.Fist),.1f,"unarmed explicit adaptation");
        Near(DuelSourceEquipment.DamageStat(Weapon("sword"),true),1,"shield source D");
        Check(DuelSourceEquipment.Surface(Weapon("sword"),false,true)==DuelReferenceRules.Surface.WoodBlunt,"wood shield");
        foreach(var spec in DuelWeapon.All){
            var physics=DuelSourceEquipment.SourcePhysics(spec);
            Near(DuelSourceEquipment.Physics(spec).bodyMass,physics.bodyMass,spec.id+" baseline mass");
            Near(physics.linearDamping,50,spec.id+" conditional source damping");
        }
        Near(DuelSourceEquipment.SourcePhysics(Weapon("greatsword")).bodyMass,2,"two sword");
        Near(DuelSourceEquipment.SourcePhysics(Weapon("greataxe")).counterweightMass,1.25f,"two axe counter");
        Near(DuelSourceEquipment.SourcePhysics(Weapon("warhammer")).bodyMass,3,"two hammer");
        Near(DuelSourceEquipment.SourcePhysics(Weapon("mace")).counterweightMass,2.5f,"one mace counter");
        Near(DuelSourceEquipment.SourcePhysics(Weapon("spear")).bodyMass,3,"two spear");
        Near(DuelSourceEquipment.SourcePhysics(Weapon("sword"),true).counterweightMass,.05f,"shield counter");
        Near(DuelSourceEquipment.Physics(Weapon("sword","wood_sword",mass:.45f)).bodyMass,.45f/.7f,"wood mass growth retained");
        Near(DuelSourceEquipment.Physics(Weapon("sword","iron_practice_sword",mass:.78f)).bodyMass,.78f/.7f,"iron mass growth retained");
        Near(DuelSourceEquipment.DroppedPhysics().bodyMass,1,"drop mass");
        Near(DuelSourceEquipment.DroppedPhysics().angularDamping,.05f,"drop damping");
        var armor=J.O("armor",J.O("template_id","leather","defense",4,"durability",100));
        Near(DuelSourceEquipment.Armor(armor,"torso").defense,4,"leather torso");
        Near(DuelSourceEquipment.Armor(armor,"head").defense,0,"no free helmet");
        Near(DuelSourceEquipment.Armor(armor,"forearmNear").defense,0,"leather vest coverage");
        armor["armor"]["durability"]=new J(0);
        Near(DuelSourceEquipment.Armor(armor,"torso").defense,0,"broken armor");
        var layers=new List<J>{
            J.O("layer",3,"coverage",new List<J>{new J("torso")},"defense",3,"material",1,"visible",false),
            J.O("layer",1,"coverage",new List<J>{new J("torso")},"defense",7,"material",6),
            J.O("layer",2,"coverage",new List<J>{new J("torso")},"defense",9,"material",7,"enabled",false)};
        var hit=DuelSourceEquipment.Armor(J.O("armor_layers",layers),"torso");
        Near(hit.defense,10,"sum present references, ignore visibility");
        Check(hit.material==1,"outermost reference wins, not material max");
        Near(DuelSourceEquipment.Armor(J.O(),"torso").defense,0,"no flattened reduction fallback");
        Console.WriteLine("Equipment runtime bridge: "+checks+" checks passed");
    }
}
