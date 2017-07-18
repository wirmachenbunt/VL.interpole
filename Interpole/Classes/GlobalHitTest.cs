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
        List<UIelement> hitestList = new List<UIelement>();

        public void hittest()
        {
            hitestList.Clear();
            hitestList.Add(myplaybar); //add playbar

            foreach(Clip myclip in clipList)
            {

            }


            var hitObject = hitestList.FirstOrDefault(x => x.Bounds.Contains(new Point(mousePos.X,mousePos.Y)));
            if (hitObject != null) FLogger.Log(LogType.Debug, hitObject.GetType().ToString());
        }

    }
}
