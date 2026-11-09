using UnityEngine;
namespace DNF60Prototype {
 public sealed class DamageNumber:MonoBehaviour {
  float born;string label;GUIStyle style;
  public void Init(int amount){born=Time.time;label=amount.ToString();}
  void Update(){transform.position+=Vector3.up*Time.deltaTime*.65f;if(Time.time-born>.7f)Destroy(gameObject);}
  void OnGUI(){if(Camera.main==null)return;var p=Camera.main.WorldToScreenPoint(transform.position);if(p.z<=0)return;if(style==null)style=new GUIStyle(GUI.skin.label){fontSize=24,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};float alpha=1-(Time.time-born)/.7f;var rect=new Rect(p.x-50,Screen.height-p.y-20,100,36);style.normal.textColor=new Color(0,0,0,alpha);GUI.Label(new Rect(rect.x+2,rect.y+2,rect.width,rect.height),label,style);style.normal.textColor=new Color(1,.86f,.42f,alpha);GUI.Label(rect,label,style);}
 }
}
