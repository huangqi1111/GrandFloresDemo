using UnityEngine;

namespace DNF60Prototype
{
    // Geometry is separate from the melee/ice sprites. The ground target never follows the hero.
    public sealed class ForestLightning : MonoBehaviour
    {
        LineRenderer warning,halo,core,branch;
        Material material;
        public void Build(int order)
        {
            material=new Material(Shader.Find("Sprites/Default"));
            warning=Line("Ground warning",.035f,order);
            warning.loop=true;warning.positionCount=48;
            for(int i=0;i<48;i++){
                float a=i*Mathf.PI*2/48;
                warning.SetPosition(i,new Vector3(Mathf.Cos(a)*.88f,Mathf.Sin(a)*.39f,0));
            }
            halo=Line("Electric glow",.17f,order+1);
            core=Line("Lightning core",.045f,order+2);
            branch=Line("Lightning fork",.025f,order+2);
            var points=new[]{new Vector3(.22f,4.8f),new Vector3(-.15f,3.7f),new Vector3(.2f,3.3f),new Vector3(-.26f,2.5f),new Vector3(.1f,2.6f),new Vector3(-.18f,1.5f),new Vector3(.2f,1.7f),Vector3.zero};
            halo.positionCount=core.positionCount=points.Length;
            halo.SetPositions(points);core.SetPositions(points);
            branch.positionCount=4;branch.SetPositions(new[]{points[3],new Vector3(-.72f,2.1f),new Vector3(-.45f,2.15f),new Vector3(-.88f,1.25f)});
            Show(0);
        }
        LineRenderer Line(string label,float width,int order)
        {
            var go=new GameObject(label);go.transform.SetParent(transform,false);
            var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;
            line.sharedMaterial=material;line.startWidth=line.endWidth=width;
            line.sortingOrder=order;line.numCornerVertices=2;line.numCapVertices=2;
            return line;
        }
        public void Show(float elapsed)
        {
            bool strike=elapsed>=.65f;
            float fade=strike?Mathf.Clamp01((1.1f-elapsed)/.45f):.35f+.2f*Mathf.Sin(elapsed*24);
            warning.startColor=warning.endColor=new Color(1,.72f,.16f,fade);
            halo.enabled=core.enabled=branch.enabled=strike;
            halo.startColor=halo.endColor=new Color(.32f,.6f,1,fade*.45f);
            core.startColor=core.endColor=new Color(.92f,.97f,1,fade);
            branch.startColor=branch.endColor=new Color(.65f,.82f,1,fade);
        }
        void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
