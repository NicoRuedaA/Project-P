using System;
using System.Collections.Generic;
using UnityEngine;
namespace Pokemon3D {
[Serializable] public class CreatureRecord {
 public string id; public int species; public string combatLoadoutId; public int level=1; public int xp; public float health=100;
 public CreatureRecord(int type){id=Guid.NewGuid().ToString("N");species=type;}
 public void GainXP(int amount){xp+=amount;while(xp>=level*40){xp-=level*40;level++;}health=100;}
}
[Serializable] public class SaveData { public int version=1; public List<CreatureRecord> party=new List<CreatureRecord>();public int selected; public int balls=20;public float x,y,z; }
public static class Species {
 public static readonly string[] Names={"Bramble","Emberfox","Tidehorn"};
 public static readonly Color[] Colors={new Color(.38f,.7f,.37f),new Color(1,.43f,.2f),new Color(.26f,.66f,.86f)};
 public static float CaptureBase(int type){return type==0?.8f:type==1?.6f:.5f;}
}
}