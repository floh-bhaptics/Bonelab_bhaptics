﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MelonLoader;
using HarmonyLib;
using MyBhapticsTactsuit;
using UnityEngine;

namespace Bonelab_bhaptics
{
    public class Bonelab_bhaptics : MelonMod
    {
        public static TactsuitVR tactsuitVr;
        public static bool playerRightHanded = true;

        public override void OnInitializeMelon()
        {
            //base.OnApplicationStart();
            tactsuitVr = new TactsuitVR();
            tactsuitVr.PlaybackHaptics("HeartBeat");
        }

        [HarmonyPatch(typeof(SLZ.Props.Weapons.Gun), "Fire", new Type[] { })]
        public class bhaptics_FireGun
        {
            [HarmonyPostfix]
            public static void Postfix(SLZ.Props.Weapons.Gun __instance)
            {
                bool rightHanded = false;
                bool twoHanded = false;
                bool supportHand = false;

                twoHanded = (__instance.triggerGrip.attachedHands.Count > 1);
                
                foreach (var myHand in __instance.triggerGrip.attachedHands)
                {
                    if (myHand.handedness == SLZ.Handedness.RIGHT) rightHanded = true;
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
                float intensity = __instance.kickForce / 12.0f;

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

        [HarmonyPatch(typeof(PlayerDamageReceiver), "ReceiveAttack", new Type[] { typeof(SLZ.Combat.Attack) })]
        public class bhaptics_ReceiveAttack
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerDamageReceiver __instance, SLZ.Combat.Attack attack)
            {
                string damagePattern = "Impact";
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
                if (__instance.bodyPart == PlayerDamageReceiver.BodyPart.Head)
                {
                    if (tactsuitVr.faceConnected)
                    {
                        tactsuitVr.PlaybackHaptics("Headshot_F");
                    }
                }
                if (__instance.bodyPart == PlayerDamageReceiver.BodyPart.LeftArm)
                {
                    if (tactsuitVr.armsConnected)
                    {
                        tactsuitVr.PlaybackHaptics("Recoil_L");
                        return;
                    }
                }
                if (__instance.bodyPart == PlayerDamageReceiver.BodyPart.RightArm)
                {
                    if (tactsuitVr.armsConnected)
                    {
                        tactsuitVr.PlaybackHaptics("Recoil_R");
                        return;
                    }
                }

                var angleShift = getAngleAndShift(__instance.transform, attack.collider.transform.position);
                tactsuitVr.PlayBackHit(damagePattern, angleShift.Key, angleShift.Value);
                __instance.health.TAKEDAMAGE(attack.damage);
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

        [HarmonyPatch(typeof(SLZ.Data.SaveData.PlayerSettings), "OnPropertyChanged", new Type[] { typeof(string) })]
        public class bhaptics_PropertyChanged
        {
            [HarmonyPostfix]
            public static void Postfix(SLZ.Data.SaveData.PlayerSettings __instance)
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
                tactsuitVr.StopThreads();
            }
        }

        [HarmonyPatch(typeof(Player_Health), "Update", new Type[] { })]
        public class bhaptics_PlayerHealthUpdate
        {
            [HarmonyPostfix]
            public static void Postfix(Player_Health __instance)
            {
                if (__instance.curr_Health <= 0.3f * __instance.max_Health) tactsuitVr.StartHeartBeat();
                else tactsuitVr.StopHeartBeat();
            }
        }
        
    }
}