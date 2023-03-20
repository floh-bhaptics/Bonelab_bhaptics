using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MelonLoader;
using HarmonyLib;
using MyBhapticsTactsuit;
using UnityEngine;
using SLZ.Rig;

namespace Bonelab_bhaptics
{
    public class Bonelab_bhaptics : MelonMod
    {
        public static TactsuitVR tactsuitVr;
        public static bool playerRightHanded = true;
        public static RigManager myRigManager = null;

        public override void OnInitializeMelon()
        {
            tactsuitVr = new TactsuitVR();
            tactsuitVr.PlaybackHaptics("HeartBeat");
        }

        [HarmonyPatch(typeof(SLZ.Rig.RigManager), "Awake", new Type[] { })]
        public class bhaptics_GameControlRigManager
        {
            [HarmonyPostfix]
            public static void Postfix(SLZ.Rig.RigManager __instance)
            {
                if (myRigManager == null)
                {
                    tactsuitVr.LOG("Player RigManager initialized... " + __instance.name);
                    myRigManager = __instance;
                }
            }
        }


        [HarmonyPatch(typeof(SLZ.Props.Weapons.Gun), "Fire", new Type[] { })]
        public class bhaptics_FireGun
        {
            [HarmonyPostfix]
            public static void Postfix(SLZ.Props.Weapons.Gun __instance)
            {
                bool rightHanded = false;
                bool twoHanded = false;
                bool supportHand = __instance._isSlideGrabbed;

                if (__instance == null) return;
                if (__instance.triggerGrip == null) return;
                try { if ((__instance.AmmoCount() <= 0) && (__instance.chamberedCartridge == null)) return; }
                catch { tactsuitVr.LOG("NullReference in AmmoCount."); return; }
                twoHanded = (__instance.triggerGrip.attachedHands.Count > 1);
                foreach (var myHand in __instance.triggerGrip.attachedHands)
                {
                    if (myHand.handedness == SLZ.Handedness.RIGHT) rightHanded = true;
                    if (myRigManager != null)
                        if (myHand.manager != myRigManager) return;
                }

                if (__instance.otherGrips != null)
                {
                    foreach (var myGrip in __instance.otherGrips)
                    {
                        if (myGrip.attachedHands.Count > 0)
                        {
                            foreach (var myHand in myGrip.attachedHands)
                            {
                                if ((myHand.handedness == SLZ.Handedness.LEFT) && (rightHanded)) supportHand = true;
                                if ((myHand.handedness == SLZ.Handedness.RIGHT) && (!rightHanded)) supportHand = true;
                            }
                        }
                    }
                }

                //tactsuitVr.LOG("Kickforce: " + __instance.kickForce.ToString());
                float intensity = Mathf.Min(Mathf.Max(__instance.kickForce / 12.0f, 1.0f), 0.5f);

                tactsuitVr.GunRecoil(rightHanded, intensity, twoHanded, supportHand);
            }
        }

        private static KeyValuePair<float, float> getAngleAndShift(Transform player, Vector3 hit)
        {
            // bhaptics pattern starts in the front, then rotates to the left. 0° is front, 90° is left, 270° is right.
            // y is "up", z is "forward" in local coordinates
            Vector3 patternOrigin = new Vector3(0f, 0f, 1f);
            Vector3 hitPosition = hit - player.position;
            Quaternion myPlayerRotation = player.rotation;
            Vector3 playerDir = myPlayerRotation.eulerAngles;
            // get rid of the up/down component to analyze xz-rotation
            Vector3 flattenedHit = new Vector3(hitPosition.x, 0f, hitPosition.z);

            // get angle. .Net < 4.0 does not have a "SignedAngle" function...
            float hitAngle = Vector3.Angle(flattenedHit, patternOrigin);
            // check if cross product points up or down, to make signed angle myself
            Vector3 crossProduct = Vector3.Cross(flattenedHit, patternOrigin);
            if (crossProduct.y < 0f) { hitAngle *= -1f; }
            // relative to player direction
            float myRotation = hitAngle - playerDir.y;
            // switch directions (bhaptics angles are in mathematically negative direction)
            myRotation *= -1f;
            // convert signed angle into [0, 360] rotation
            if (myRotation < 0f) { myRotation = 360f + myRotation; }


            // up/down shift is in y-direction
            // in Battle Sister, the torso Transform has y=0 at the neck,
            // and the torso ends at roughly -0.5 (that's in meters)
            // so cap the shift to [-0.5, 0]...
            float hitShift = hitPosition.y;
            //tactsuitVr.LOG("HitShift: " + hitShift.ToString());
            float upperBound = 0.5f;
            float lowerBound = -0.5f;
            if (hitShift > upperBound) { hitShift = 0.5f; }
            else if (hitShift < lowerBound) { hitShift = -0.5f; }
            // ...and then spread/shift it to [-0.5, 0.5], which is how bhaptics expects it
            else { hitShift = (hitShift - lowerBound) / (upperBound - lowerBound) - 0.5f; }

            // No tuple returns available in .NET < 4.0, so this is the easiest quickfix
            return new KeyValuePair<float, float>(myRotation, hitShift);
        }

        /*
        [HarmonyPatch(typeof(Player_Health), "TAKEDAMAGE", new Type[] { typeof(float) })]
        public class bhaptics_TakeDamage
        {
            [HarmonyPostfix]
            public static void Postfix(Player_Health __instance, float damage)
            {
                tactsuitVr.LOG("PlayerHealth damage: " + __instance.curr_Health.ToString() + " " + damage.ToString());
            }
        }
        */

        [HarmonyPatch(typeof(PlayerDamageReceiver), "ReceiveAttack", new Type[] { typeof(SLZ.Combat.Attack) })]
        public class bhaptics_ReceiveAttack
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerDamageReceiver __instance, SLZ.Combat.Attack attack)
            {
                if (myRigManager != null)
                    if (__instance.health._rigManager != myRigManager) return;
                float armDamage = 0.2f;
                float headDamage = 0.8f;
                float bodyDamage = 0.5f;
                string damagePattern;
                bool hapticsApplied = false;
                switch (attack.attackType)
                {
                    case SLZ.Marrow.Data.AttackType.Piercing:
                        damagePattern = "BulletHit";
                        break;
                    case SLZ.Marrow.Data.AttackType.Blunt:
                        damagePattern = "Impact";
                        break;
                    case SLZ.Marrow.Data.AttackType.Electric:
                        damagePattern = "ElectricHit";
                        break;
                    case SLZ.Marrow.Data.AttackType.Explosive:
                        damagePattern = "ExplosionFace";
                        break;
                    case SLZ.Marrow.Data.AttackType.Fire:
                        damagePattern = "LavaballHit";
                        break;
                    case SLZ.Marrow.Data.AttackType.Ice:
                        damagePattern = "FreezeHit";
                        break;
                    case SLZ.Marrow.Data.AttackType.Slicing:
                        damagePattern = "BladeHit";
                        break;
                    case SLZ.Marrow.Data.AttackType.Stabbing:
                        damagePattern = "BulletHit";
                        break;
                    default:
                        damagePattern = "Impact";
                        break;
                }
                float absoluteDamage = Math.Abs(attack.damage);
                if (__instance.bodyPart == PlayerDamageReceiver.BodyPart.Head)
                {
                    if (tactsuitVr.faceConnected)
                    {
                        tactsuitVr.PlaybackHaptics("Headshot_F");
                        hapticsApplied = true;
                        absoluteDamage *= headDamage;
                    }
                }
                if (__instance.bodyPart == PlayerDamageReceiver.BodyPart.LeftArm)
                {
                    if (tactsuitVr.armsConnected)
                    {
                        tactsuitVr.PlaybackHaptics("Recoil_L");
                        hapticsApplied = true;
                        absoluteDamage *= armDamage;
                    }
                }
                if (__instance.bodyPart == PlayerDamageReceiver.BodyPart.RightArm)
                {
                    if (tactsuitVr.armsConnected)
                    {
                        tactsuitVr.PlaybackHaptics("Recoil_R");
                        hapticsApplied = true;
                        absoluteDamage *= armDamage;
                    }
                }

                if ((!hapticsApplied) && (attack.collider != null))
                {
                    var angleShift = getAngleAndShift(__instance.transform, attack.collider.transform.position);
                    tactsuitVr.PlayBackHit(damagePattern, angleShift.Key, angleShift.Value);
                    absoluteDamage *= bodyDamage;
                }
                __instance.health.TAKEDAMAGE(absoluteDamage);
            }
        }

        
        [HarmonyPatch(typeof(SLZ.Interaction.InventorySlotReceiver), "OnHandGrab", new Type[] { typeof(SLZ.Interaction.Hand) })]
        public class bhaptics_SlotGrab
        {
            [HarmonyPostfix]
            public static void Postfix(SLZ.Interaction.InventorySlotReceiver __instance, SLZ.Interaction.Hand hand)
            {
                if (__instance.isInUIMode) return;
                if (hand == null) return;
                if (myRigManager != null)
                    if (hand.manager != myRigManager) return;
                bool rightHand = (hand.handedness == SLZ.Handedness.RIGHT);
                if (__instance.slotType == SLZ.Props.Weapons.WeaponSlot.SlotType.SIDEARM)
                {
                    if (rightHand) tactsuitVr.PlaybackHaptics("GrabGun_L");
                    else tactsuitVr.PlaybackHaptics("GrabGun_R");
                }
                else
                {
                    if (rightHand) tactsuitVr.PlaybackHaptics("ReceiveShoulder_R");
                    else tactsuitVr.PlaybackHaptics("ReceiveShoulder_L");
                }
            }
        }

        [HarmonyPatch(typeof(SLZ.Interaction.InventorySlotReceiver), "OnHandDrop", new Type[] { typeof(SLZ.Interaction.IGrippable) })]
        public class bhaptics_SlotInsert
        {
            [HarmonyPostfix]
            public static void Postfix(SLZ.Interaction.InventorySlotReceiver __instance, SLZ.Interaction.IGrippable host)
            {
                if (__instance == null) return;
                if (__instance.isInUIMode) return;
                if (host == null) return;
                SLZ.Interaction.Hand hand = host.GetLastHand();
                if (hand == null) return;
                if (myRigManager != null)
                    if (hand.manager != myRigManager) return;
                bool rightHand = (hand.handedness == SLZ.Handedness.RIGHT);
                if (__instance.slotType == SLZ.Props.Weapons.WeaponSlot.SlotType.SIDEARM)
                {
                    if (rightHand) tactsuitVr.PlaybackHaptics("StoreGun_L");
                    else tactsuitVr.PlaybackHaptics("StoreGun_R");
                }
                else
                {
                    if (rightHand) tactsuitVr.PlaybackHaptics("StoreShoulder_R");
                    else tactsuitVr.PlaybackHaptics("StoreShoulder_L");
                }
            }
        }

        /*
        [HarmonyPatch(typeof(BaseGameController), "OnSlowTime", new Type[] { typeof(float) })]
        public class bhaptics_SlowTime
        {
            [HarmonyPostfix]
            public static void Postfix(BaseGameController __instance)
            {
                tactsuitVr.PlaybackHaptics("SloMo");
            }
        }
        */

        [HarmonyPatch(typeof(SLZ.SaveData.PlayerSettings), "OnPropertyChanged", new Type[] { typeof(string) })]
        public class bhaptics_PropertyChanged
        {
            [HarmonyPostfix]
            public static void Postfix(SLZ.SaveData.PlayerSettings __instance)
            {
                playerRightHanded = __instance.RightHanded;
            }
        }



        [HarmonyPatch(typeof(Player_Health), "Death", new Type[] {  })]
        public class bhaptics_PlayerDeath
        {
            [HarmonyPostfix]
            public static void Postfix(Player_Health __instance)
            {
                if (myRigManager != null)
                    if (__instance._rigManager != myRigManager) return;
                tactsuitVr.StopThreads();
            }
        }

        [HarmonyPatch(typeof(Player_Health), "Update", new Type[] { })]
        public class bhaptics_PlayerHealthUpdate
        {
            [HarmonyPostfix]
            public static void Postfix(Player_Health __instance)
            {
                if (myRigManager != null)
                    if (__instance._rigManager != myRigManager) return;
                if (__instance.curr_Health <= 0.3f * __instance.max_Health) tactsuitVr.StartHeartBeat();
                else tactsuitVr.StopHeartBeat();
            }
        }

        [HarmonyPatch(typeof(SLZ.Props.PullCordDevice), "SwapAvatar", new Type[] { typeof(SLZ.Marrow.Warehouse.AvatarCrateReference) })]
        public class bhaptics_SwapAvatar
        {
            [HarmonyPostfix]
            public static void Postfix(SLZ.Props.PullCordDevice __instance)
            {
                if (myRigManager != null)
                    if (__instance.rm != myRigManager) return;
                tactsuitVr.PlaybackHaptics("SwitchAvatar");
            }
        }

    }
}
