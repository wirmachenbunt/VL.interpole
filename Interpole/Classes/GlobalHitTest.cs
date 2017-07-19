using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using VVVV.Core.Logging;

namespace VVVV.Nodes
{
    partial class Interpole
    {
        List<UIelement> hitTestList = new List<UIelement>();
        dynamic hitObject;

        public void hittest()
        {
            hitTestList.Clear();
            hitTestList.Add(myplaybar); //add playbar

            foreach(Clip myclip in clipList)
            {
                hitTestList.Add(myclip); //add all clips, this needs to be culled at some point
            }

            
            hitObject = hitTestList.FirstOrDefault(x => x.Bounds.Contains(new Point(mousePos.X,mousePos.Y))); // the actual hitTest
         
        }

    }
}
