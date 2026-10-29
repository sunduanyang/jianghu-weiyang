using System;
using UnityEngine;

namespace Jianghu {
// Pure visual geometry. These matrices never reference a rigidbody or joint.
public static class CharacterSkinStrain {
    public struct Triangle {
        public int A,B,C;
        public double I00,I01,I10,I11;
        public bool Valid;
    }
    public static Triangle[] Bind(Vector3[] points,int[] indices){
        if(points==null||indices==null||indices.Length%3!=0)throw new ArgumentException("Painted triangles need complete vertex/index arrays");
        var result=new Triangle[indices.Length/3];
        for(int i=0;i<result.Length;i++){
            int a=indices[i*3],b=indices[i*3+1],c=indices[i*3+2];
            double x=points[b].x-points[a].x,y=points[b].y-points[a].y;
            double u=points[c].x-points[a].x,v=points[c].y-points[a].y,det=x*v-u*y;
            if(double.IsNaN(det)||double.IsInfinity(det)||Math.Abs(det)<=1e-10)throw new ArgumentException("Degenerate painted bind triangle: "+i);
            var t=new Triangle{A=a,B=b,C=c,Valid=true,I00=v/det,I01=-u/det,I10=-y/det,I11=x/det};
            result[i]=t;
        }
        return result;
    }
    static double Norm(double a,double b,double c,double d){
        double x=a*a+c*c,y=b*b+d*d,z=a*b+c*d;
        return Math.Sqrt(Math.Max(0,(x+y+Math.Sqrt((x-y)*(x-y)+4*z*z))*.5));
    }
    public static double DifferentialNorm(Triangle t,Vector3[] rigid,Vector3[] candidate){
        if(!t.Valid)return 0;
        double x=(candidate[t.B].x-candidate[t.A].x)-(rigid[t.B].x-rigid[t.A].x);
        double y=(candidate[t.B].y-candidate[t.A].y)-(rigid[t.B].y-rigid[t.A].y);
        double u=(candidate[t.C].x-candidate[t.A].x)-(rigid[t.C].x-rigid[t.A].x);
        double v=(candidate[t.C].y-candidate[t.A].y)-(rigid[t.C].y-rigid[t.A].y);
        return Norm(x*t.I00+u*t.I10,x*t.I01+u*t.I11,y*t.I00+v*t.I10,y*t.I01+v*t.I11);
    }
    public static void Stretch(Triangle t,Vector3[] points,float inverseScale,out float minimum,out float maximum){
        minimum=maximum=1;if(!t.Valid)return;
        double x=(points[t.B].x-points[t.A].x)*inverseScale,y=(points[t.B].y-points[t.A].y)*inverseScale;
        double u=(points[t.C].x-points[t.A].x)*inverseScale,v=(points[t.C].y-points[t.A].y)*inverseScale;
        double a=x*t.I00+u*t.I10,b=x*t.I01+u*t.I11,c=y*t.I00+v*t.I10,d=y*t.I01+v*t.I11;
        double largest=Norm(a,b,c,d);maximum=(float)largest;
        minimum=largest>1e-15?(float)(Math.Abs(a*d-b*c)/largest):0;
    }
}
}
