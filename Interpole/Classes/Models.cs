using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace VVVV.Nodes
{
    public partial class Interpole
    {
        public abstract class UIelement
        {
            public string ID
            {
                get;
                set;
            }


            public string Name
            {
                get;
                set;
            }

            public Rectangle Bounds
            {
                get;
                set;
            }
        }
        public class Clip : UIelement
        {
            public int StartFrame
            {
                get;
                set;
            }

            public int Duration
            {
                get;
                set;
            }

            public int FadeIn
            {
                get;
                set;
            }

            public int FadeOut
            {
                get;
                set;
            }

            public string Color
            {
                get;
                set;
            }

            public string Variable
            {
                get;
                set;
            }

            public List<int> ReferenceClipPos
            {
                get;
                set;
            }
        }
        public class Playbar : UIelement
        {

        }
        public class TimeCodeDisplay : UIelement
        {
            
        }
        public class ZoomBar :UIelement
        {

        }
        public class WorkSpace : UIelement
        {

        }
        public class TimeCode
        {
            public int Hours
            {
                get;
                set;
            }

            public int Minutes
            {
                get;
                set;
            }

            public int Seconds
            {
                get;
                set;
            }

            public int Frames
            {
                get;
                set;
            }

        }
    }
}
