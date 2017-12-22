using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace Interpole
{
    public partial class InterpoleForm
    {
        public class InterpoleData
        {
            public int PlayHeadPosition { get; set; }

            public Point WindowLocation { get; set; }
            public Size WSize { get; set; } //window size
            public bool UILock { get; set; } // locking the UI 

            public bool Loop { get; set; } //looping timeline
            public int LoopStart { get; set; } 
            public int LoopEnd { get; set; }

            public List<Marker> MarkerList { get; set; } //List of marker, later used for Automata
            public List<Clip> ClipList { get; set; } // the most important part
        }

// UI OBJECTS //////////////////////////////////////////////////////////////////////////////////////////////////////
        public abstract class UIelement //all UI Elements should be based upon this to make the hittest work
        {
            public string ID { get; set; }          
            public string Name { get; set; }
            public Rectangle Bounds { get; set; }
        }

        public class Clip : UIelement
        {
            public int StartFrame { get; set; }            
            public int Duration { get; set; }            
            public int FadeIn { get; set; }          
            public int FadeOut { get; set; }        
            public string Color { get; set; }            
            public string Variable { get; set; }            
        }

        public class Marker : UIelement //timeline markers
        {
            public int Position { get; set; }
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
