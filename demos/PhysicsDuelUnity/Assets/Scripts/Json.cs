using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jianghu {
// Small JSON DOM; no registry packages or network downloads required.
public sealed class J {
    public object value;
    public J(object v = null) { value = v; }
    public J this[string k] { get { return value is Dictionary<string,J> d && d.TryGetValue(k, out var v) ? v : new J(); } set { if (!(this.value is Dictionary<string,J>)) this.value = new Dictionary<string,J>(); ((Dictionary<string,J>)this.value)[k] = value; } }
    public J this[int i] => value is List<J> a && i >= 0 && i < a.Count ? a[i] : new J();
    public string S => value == null ? "" : value is string s ? s : Convert.ToString(value, CultureInfo.InvariantCulture);
    public int I => (int)N;
    public double N => double.TryParse(S, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0;
    public bool B => value is bool b ? b : N != 0;
    public bool Has => value != null;
    public List<J> A => value as List<J> ?? new List<J>();
    public Dictionary<string,J> D => value as Dictionary<string,J> ?? new Dictionary<string,J>();
    public static J O(params object[] args) { var d = new Dictionary<string,J>(); for(int i=0;i<args.Length;i+=2) d[(string)args[i]] = args[i+1] as J ?? new J(args[i+1]); return new J(d); }
    public override string ToString() => Write(this);
    public static string Write(J j) {
        if (j == null || j.value == null) return "null";
        if (j.value is Dictionary<string,J> d) { var a = new List<string>(); foreach(var p in d) a.Add(Quote(p.Key)+":"+Write(p.Value)); return "{"+string.Join(",",a)+"}"; }
        if (j.value is List<J> l) { var a = new List<string>(); foreach(var p in l) a.Add(Write(p)); return "["+string.Join(",",a)+"]"; }
        if (j.value is string s) return Quote(s);
        if (j.value is bool b) return b ? "true" : "false";
        return Convert.ToString(j.value, CultureInfo.InvariantCulture);
    }
    static string Quote(string s) { var b = new StringBuilder("\""); foreach(char c in s) { switch(c) {case '"':b.Append("\\\"");break;case '\\':b.Append("\\\\");break;case '\n':b.Append("\\n");break;case '\r':b.Append("\\r");break;case '\t':b.Append("\\t");break;default: if(c<32) b.Append("\\u"+((int)c).ToString("x4")); else b.Append(c);break;} } return b.Append('"').ToString(); }
    public static J Parse(string text) => new Reader(text).Read();
    sealed class Reader {
        readonly string s; int p;
        public Reader(string text) {s=text;}
        void Space() {while(p<s.Length && char.IsWhiteSpace(s[p]))p++;}
        public J Read() { Space(); if(p>=s.Length) throw new FormatException("Incomplete JSON"); char c=s[p++];
            if(c=='{') {var d=new Dictionary<string,J>(); Space();if(s[p]=='}'){p++;return new J(d);}while(true){Space();if(s[p++]!='"')throw new FormatException("JSON key");string k=Str();Space();if(s[p++]!=':')throw new FormatException("JSON colon");d[k]=Read();Space();char e=s[p++];if(e=='}')break;if(e!=',')throw new FormatException("JSON comma");}return new J(d);}
            if(c=='[') {var a=new List<J>();Space();if(s[p]==']'){p++;return new J(a);}while(true){a.Add(Read());Space();char e=s[p++];if(e==']')break;if(e!=',')throw new FormatException("JSON comma");}return new J(a);}
            if(c=='"')return new J(Str());
            int start=p-1;while(p<s.Length && !char.IsWhiteSpace(s[p]) && ",]}".IndexOf(s[p])<0)p++;string token=s.Substring(start,p-start);
            if(token=="null")return new J();if(token=="true")return new J(true);if(token=="false")return new J(false);
            return new J(double.Parse(token,CultureInfo.InvariantCulture));
        }
        string Str(){var b=new StringBuilder();while(p<s.Length){char c=s[p++];if(c=='"')return b.ToString();if(c!='\\'){b.Append(c);continue;}char e=s[p++];switch(e){case 'u':b.Append((char)int.Parse(s.Substring(p,4),NumberStyles.HexNumber));p+=4;break;case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;default:b.Append(e);break;}}throw new FormatException("JSON string");}
    }
}
}
