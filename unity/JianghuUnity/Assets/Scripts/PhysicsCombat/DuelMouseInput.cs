using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        bool MouseMain=>human&&!aiDriving&&game.controls.mouse.main.hasTarget;
        bool MouseOff=>human&&!aiDriving&&game.controls.mouse.off.hasTarget;
        public Vector2 MouseSeed(bool primary){
            string side=primary?"Near":"Far";
            return DuelMouseHands.Reach((limbs["hand"+side].A-limbs["upper"+side].A)/MouseReach(primary));
        }
        public float MouseReach(bool primary){string side=primary?"Near":"Far";return limbs["upper"+side].length+limbs["forearm"+side].length-.002f;}
        public float MousePixels(bool primary)=>MouseReach(primary)*Unit;
        public float MouseAxis(bool primary)=>(!primary&&shield!=null?shield:sword).Rotation+90+(!primary&&HasShield?-facing*90:0);
        void AdvanceMouseTargets(float dt){
            if(!human||aiDriving||!game.Live)return;
            float load=.8f+Mathf.Sqrt(weapon.mass)*.2f;
            if(game.Clock<mainResistanceUntil)load+=1.1f;
            float offLoad=.8f+Mathf.Sqrt(HasShield?1.05f:DuelWeapon.Fist.mass)*.2f;
            if(game.Clock<offResistanceUntil)offLoad+=1.1f;
            game.controls.mouse.Advance(dt,load,offLoad);
            // Brake actual momentum with a bounded opposing torque; never
            // overwrite body velocity or fight the other actively held hand.
            if(weapon.TwoHanded){if(!game.controls.mouse.IsControlled(true)&&!game.controls.mouse.IsControlled(false)&&MouseMain)BrakeReleased(sword);}
            else {
                if(MouseMain&&!game.controls.mouse.IsControlled(true))BrakeReleased(sword);
                if(MouseOff&&!game.controls.mouse.IsControlled(false)&&shield!=null)BrakeReleased(shield);
            }
        }
        void BrakeReleased(Limb item){
            float spin=(item.rb.angularVelocity-torso.rb.angularVelocity)*Mathf.Deg2Rad;
            item.rb.AddTorque(Mathf.Clamp(-spin*item.rb.inertia*8,-12*ArmPower,12*ArmPower));
        }
        void RestoreMousePose(){
            if(!human)return;
            game.controls.mouse.Clear();
            game.controls.mouse.Seed(true,MouseSeed(true),MouseAxis(true));game.controls.mouse.Seed(false,MouseSeed(false),MouseAxis(false));
        }
    }
}
