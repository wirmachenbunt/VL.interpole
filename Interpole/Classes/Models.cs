using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace VVVV.Nodes
{
    partial class Interpole
    {
        public class Clip
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

            public int StartFrame
            {
                get;
                set;
            }

            public int EndFrame
            {
                get;
                set;
            }

            public Color ClipColor
            {
                get;
                set;
            }
        }
    }
}
