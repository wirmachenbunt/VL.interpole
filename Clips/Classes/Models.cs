using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace VVVV.Nodes
{
    partial class Clips
    {
        public class Clip
        {
            public int Name
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
