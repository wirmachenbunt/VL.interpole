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
        List<Clip> selectionList = new List<Clip>();
        Rectangle selectionRectangle = new Rectangle(); //multiselection rectangle
        dynamic hitObject;
        dynamic rollOverObject;



        public void hittest()
        {
            hitTestList.Clear();

            #region addingHitobjects
            hitTestList.Add(myplaybar); //add playbar
            hitTestList.Add(mytimecode); //add timecode


            foreach (Clip myclip in clipList)
            {
                hitTestList.Add(myclip); //add all clips, this needs to be culled at some point
            }

            hitTestList.Add(myworkspace); // workspace 
            #endregion

            // the actual hitTest
            hitObject = hitTestList.FirstOrDefault(x => x.Bounds.Contains(new Point(mousePos.X,mousePos.Y))); 
            //FLogger.Log(LogType.Debug, hitObject.GetType().ToString());
        }

        public void hittestRollover()
        {
            List<UIelement> myhitTestList = new List<UIelement>();

            foreach (Clip myclip in clipList)
            {
                myhitTestList.Add(myclip); //add all clips, this needs to be culled at some point
            }
            // the actual hitTest
            rollOverObject = myhitTestList.FirstOrDefault(x => x.Bounds.Contains(new Point(mousePos.X, mousePos.Y)));

            if(rollOverObject is Clip && hitObject == null)
            {
                int start = (rollOverObject.Bounds.X + rollOverObject.Bounds.Width) - (5);
                int end = rollOverObject.Bounds.X + rollOverObject.Bounds.Width;

                if (mousePos.X > start && mousePos.X < end && selectionList.Count == 0)
                {
                    this.Cursor = Cursors.SizeWE;
                }
            }
            
        }

        public void MultiSelectionClips()
        {
            selectionList = clipList.Where(x => x.Bounds.IntersectsWith(selectionRectangle)).ToList();
        }


    }
}
