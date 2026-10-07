using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PloSyntax {
    // Parser for the generated rank/card/suit subset; no executable source is accepted.
    public sealed class SyntaxMatcher {
        abstract class Node { public abstract bool Match(long hand); }
        sealed class Constant:Node { public bool Value; public override bool Match(long h){return Value;} }
        sealed class Negation:Node { public Node Child; public override bool Match(long h){return !Child.Match(h);} }
        sealed class Group:Node { public Node[] Children;public bool Union;public override bool Match(long h){foreach(var c in Children)if(c.Match(h)==Union)return Union;return !Union;} }
        sealed class Atom:Node {
            public long[] Masks;
            bool Search(long hand,int index){if(index==Masks.Length)return true;long candidates=hand&Masks[index];while(candidates!=0){long bit=candidates&-candidates;if(Search(hand^bit,index+1))return true;candidates^=bit;}return false;}
            public override bool Match(long hand){return Search(hand,0);}
        }
        readonly string source;int at,depth;readonly Node root;
        public SyntaxMatcher(string syntax){source=Regex.Replace(syntax??"",@"\s+","");if(source.Length==0||source.Length>2000000)throw new ArgumentException("Empty or oversized syntax");root=Expression();if(at!=source.Length)throw new ArgumentException("Unexpected syntax at "+at);}
        bool Take(char c){if(at<source.Length&&source[at]==c){at++;return true;}return false;}
        Node Expression(){var list=new List<Node>{Intersection()};while(Take(','))list.Add(Intersection());return list.Count==1?list[0]:new Group{Children=list.ToArray(),Union=true};}
        Node Intersection(){var list=new List<Node>{Primary()};while(at<source.Length){if(Take(':'))list.Add(Primary());else if(Take('!'))list.Add(new Negation{Child=Primary()});else break;}return list.Count==1?list[0]:new Group{Children=list.ToArray()};}
        Node Primary(){
            if(++depth>256)throw new ArgumentException("Syntax nesting is too deep");
            try{if(Take('!'))return new Negation{Child=Primary()};if(Take('(')){Node n=Expression();if(!Take(')'))throw new ArgumentException("Missing closing parenthesis");return n;}
                int start=at;while(at<source.Length&&":!,()".IndexOf(source[at])<0)at++;string text=source.Substring(start,at-start);
                if(text=="*")return new Constant{Value=true};if(text.Length==0)throw new ArgumentException("Empty syntax group");
                var masks=new List<long>();int i=0;
                while(i<text.Length){int r="AKQJT98765432".IndexOf(char.ToUpperInvariant(text[i])),s=-1;if(r>=0){i++;if(i<text.Length&&"shdc".IndexOf(char.ToLowerInvariant(text[i]))>=0)s="shdc".IndexOf(char.ToLowerInvariant(text[i++]));}else{s="shdc".IndexOf(char.ToLowerInvariant(text[i++]));if(s<0)throw new ArgumentException("Invalid syntax token");}
                    long mask=0;for(int rr=0;rr<13;rr++)for(int ss=0;ss<4;ss++)if((r<0||rr==r)&&(s<0||ss==s))mask|=1L<<(rr*4+ss);masks.Add(mask);
                }
                if(masks.Count>4)throw new ArgumentException("A pattern can use at most four cards");return new Atom{Masks=masks.ToArray()};
            }finally{depth--;}
        }
        public bool Matches(long hand){return root.Match(hand);}
        public bool Match(string hand){return Matches(HandMask(hand));}
        public static long HandMask(string hand){
            if(hand==null||hand.Length!=8)throw new ArgumentException("Invalid PLO4 hand");long mask=0;
            for(int i=0;i<8;i+=2){int r="AKQJT98765432".IndexOf(hand[i]),s="shdc".IndexOf(hand[i+1]);if(r<0||s<0)throw new ArgumentException("Invalid card");long bit=1L<<(r*4+s);if((mask&bit)!=0)throw new ArgumentException("Repeated card");mask|=bit;}return mask;
        }
    }
}
