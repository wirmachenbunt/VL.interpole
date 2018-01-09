using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using VVVV.Core.Logging;

namespace Interpole
{
    partial class InterpoleForm
    {
        public Point mousePos; //current Mouse Pos
        public Point prevMousePos; //previous Mouse Pos
        public Point holdMousePos; // hold mouse for selection rectangle
        public Point deltaMousePos; // better delta variable, get rid of prevMouse

        private void Form1_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {

            prevMousePos = MousePosition; // for delta calculation below
            holdMousePos = mousePos; //keep last Mouseposition for rectangle selection

            //global hittest for everything
            hittest(); 

            // update snap positions
            UpdateSnapList(); 

            // set playhead
            SetPlayPosition();

            //delete clip
            if (e.Button == MouseButtons.Middle && hitObject is Clip && !ipD.UILock) DeleteClip(hitObject);

            //selection aufheben
            if (e.Button == MouseButtons.Left && hitObject is WorkSpace || e.Button == MouseButtons.Middle) selectionList.Clear();

            // is hitobject a clip and not part of the selection, clear selection
            if (hitObject is Clip) if (!selectionList.Any(cus => cus == hitObject)) selectionList.Clear();

            //DragDuration
            if (e.Button == MouseButtons.Left && hitObject is Clip && selectionList.Count == 0 && !ipD.UILock) DragDuration(hitObject);
          
        }

        private void Form1_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (hitObject is Clip && rememberLastFunction == "dragclip")
            {
                UpdateClipXML();
            }

            InterpoleXML = SerializeInterpoleData(ipD);

            rememberLastFunction = "idle";

            this.Cursor = Cursors.Default;

            hitObject = null; //clear hitObject in case there is no button pressed

            dragDurationClip = null;

            selectionRectangle.Size = new Size(0, 0);

            
        }

        private void Form1_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (hitObject is WorkSpace && e.Button == MouseButtons.Left && !ipD.UILock) AddClip(); //ADD CLIP
            if (hitObject is Clip && e.Button == MouseButtons.Left && !ipD.UILock) EditClip(hitObject); // EDIT CLIP
            if (hitObject is TimeCodeDisplay && e.Button == MouseButtons.Left && !ipD.UILock) EditTimeCode(); // EDIT TIMECODE
            
        }

        private void Form1_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            float oldzoom = Zoom;

            if (e.Delta > 0)
            {
                Zoom += 0.09F;
            }
            if (e.Delta < 0)
            {
                Zoom = Math.Max(Zoom - 0.09F, 0.01F);        
            }

            int x = e.X;
            int oldimagex = (int)(x / oldzoom);
            int newimagex = (int)(x / Zoom);
            StagePos.x = newimagex - oldimagex + StagePos.x;


        }

        private void Form1_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            //hittestRollover();

            SetGlobalMousePos(e); // get Mouse Position and calibrate with stagepos and dpi

            CalcStagePosition(MousePosition, e.Button == MouseButtons.Right,e); // calculate delta on rightclick

            SetPlayPosition(); // set playhead

            if(!ipD.UILock) SetSelectionRectangle(e); // selection rectangle

            //dragging multiselection
            if (selectionList.Count > 0 && hitObject is Clip && !ipD.UILock)
            {
                if (rememberLastFunction != "dragclip") PushUndoStep();
                DragSelection(e);
            }

            //DragDuration
            if (e.Button == MouseButtons.Left && hitObject is Clip && selectionList.Count == 0 && !ipD.UILock)
            {
                DragDuration(hitObject);
            }
            deltaMousePos = mousePos;
        }

        private void Form_Keydown(object sender, KeyEventArgs e)
        {
            //undo function
            #region undo
            if (e.KeyCode == Keys.Z && e.Modifiers == Keys.Control && !ipD.UILock)
            {
                if (undoXML.Count > 0)
                {                  
                    string tempXML = undoXML.Pop();

                    ipD.ClipList.Clear();

                    selectionList.Clear();

                    ipD.ClipList = DeserializeClipList(tempXML);        
                    
                    rememberLastFunction = "updateXML"; //remember action
                    Invalidate();
                }
            } 
            #endregion

            //PLAYBACK
            if (e.KeyCode == Keys.Space) play = !play; // invert bool play

            //COPY
            #region copy
            if (e.KeyCode == Keys.C && e.Modifiers == Keys.Control && !ipD.UILock)
            {
                clipBoard.Clear();
                CopyClips();
            }
            #endregion

            //Paste
            #region paste
            if (e.KeyCode == Keys.V && e.Modifiers == Keys.Control && !ipD.UILock)
            {
                PasteClips();
                Invalidate();
            } 
            #endregion

            //LOOP
            if (e.KeyCode == Keys.L && e.Modifiers == Keys.Shift)
            {
                ipD.Loop = !ipD.Loop; // invert loop activate
                //Invalidate();
            }

            //Lock UI
            if (e.KeyCode == Keys.L && e.Modifiers == Keys.Control)
            {
                ipD.UILock = !ipD.UILock;
                //Invalidate();
            }

            //SET TO ZERO
            if (e.KeyCode == Keys.NumPad0)
            {
                StagePos.x = 0;
                playPosFrames = 0;
                //Invalidate();
            }

            //SET TO ZERO
            if (e.KeyCode == Keys.R)
            {
                StagePos.y = 0;     
                //Invalidate();
            }

            //DELETE CLIPS OF SELECTION
            if (e.KeyCode == Keys.Delete && selectionList.Count > 0 && !ipD.UILock)
            {
                DeleteClipSelection();
                selectionList.Clear();
                //Invalidate();
            }

            //MARKER & LOOPING
            HandleMarker(sender, e);
            HandleLoopRegion(sender, e);

        }

        public void HandleLoopRegion(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.B:
                    //FLoopRegion[0] = playPosFrames;
                    //Invalidate();
                    break;
                case Keys.N:
                    //FLoopRegion[1] = playPosFrames;
                    //Invalidate();
                    break;
            }

        }

        public void HandleMarker(object sender, KeyEventArgs e)
        {
            /*

            switch (e.KeyCode)
            {
                case Keys.NumPad1:
                    if (e.Modifiers == Keys.Control)
                    {
                        FMarker[0] = playPosFrames;
                        //Invalidate();
                        break;
                    }
                    StagePos.X = 0 - (FMarker[0] / timeZoom);
                    playPosFrames = FMarker[0];
                    //Invalidate();
                    break;
                case Keys.NumPad2:
                    if (e.Modifiers == Keys.Control)
                    {
                        FMarker[1] = playPosFrames;
                        //Invalidate();
                        break;
                    }
                    StagePos.X = 0 - (FMarker[1] / timeZoom);
                    playPosFrames = FMarker[1];
                    //Invalidate();
                    break;
                case Keys.NumPad3:
                    if (e.Modifiers == Keys.Control)
                    {
                        FMarker[2] = playPosFrames;
                        //Invalidate();
                        break;
                    }
                    StagePos.X = 0 - (FMarker[2] / timeZoom);
                    playPosFrames = FMarker[2];
                    //Invalidate();
                    break;
                case Keys.NumPad4:
                    if (e.Modifiers == Keys.Control)
                    {
                        FMarker[3] = playPosFrames;
                        //Invalidate();
                        break;
                    }
                    StagePos.X = 0 - (FMarker[3] / timeZoom);
                    playPosFrames = FMarker[3];
                    //Invalidate();
                    break;
                case Keys.NumPad5:
                    if (e.Modifiers == Keys.Control)
                    {
                        FMarker[4] = playPosFrames;
                        //Invalidate();
                        break;
                    }
                    StagePos.X = 0 - (FMarker[4] / timeZoom);
                    playPosFrames = FMarker[4];
                    //Invalidate();
                    break;
                case Keys.NumPad6:
                    if (e.Modifiers == Keys.Control)
                    {
                        FMarker[5] = playPosFrames;
                        //Invalidate();
                        break;
                    }
                    StagePos.X = 0 - (FMarker[5] / timeZoom);
                    playPosFrames = FMarker[5];
                    //Invalidate();
                    break;
                case Keys.NumPad7:
                    if (e.Modifiers == Keys.Control)
                    {
                        FMarker[6] = playPosFrames;
                        //Invalidate();
                        break;
                    }
                    StagePos.X = 0 - (FMarker[6] / timeZoom);
                    playPosFrames = FMarker[6];
                    //Invalidate();
                    break;
                case Keys.NumPad8:
                    if (e.Modifiers == Keys.Control)
                    {
                        FMarker[7] = playPosFrames;
                        //Invalidate();
                        break;
                    }
                    StagePos.X = 0 - (FMarker[7] / timeZoom);
                    playPosFrames = FMarker[7];
                    //Invalidate();
                    break;
                case Keys.NumPad9:
                    if (e.Modifiers == Keys.Control)
                    {
                        FMarker[8] = playPosFrames;
                        //Invalidate();
                        break;
                    }
                    StagePos.X = 0 - (FMarker[8] / timeZoom);
                    playPosFrames = FMarker[8];
                    //Invalidate();
                    break;
            }

    */
        }

        public void CalcStagePosition(Point mousePos, bool click, System.Windows.Forms.MouseEventArgs e)
        {
            if (click && !(hitObject is Clip) && Form.ModifierKeys != Keys.Control)
            {
                int deltaX = mousePos.X - prevMousePos.X;
                prevMousePos = mousePos;
                StagePos.x += deltaX/Zoom;
                if (StagePos.x > 0) StagePos.x = 0;
                
            }

            if (click && !(hitObject is Clip) && Form.ModifierKeys == Keys.Control)
            {
                int deltaY = mousePos.Y - prevMousePos.Y;
                prevMousePos = mousePos;
                StagePos.y += deltaY/Zoom;          
            }


        }

        public void SetGlobalMousePos(MouseEventArgs e)
        {
            mousePos.X = Convert.ToInt32((e.X / Zoom)-StagePos.x);
            mousePos.Y = Convert.ToInt32(e.Y - StagePos.y);
        }

        public void SetPlayPosition()
        {
            if (hitObject is Playbar)
            {
                playPosFrames = Convert.ToInt32(mousePos.X);

                //playPosFrames + StagePos.x) *Zoom

                if (mousePos.X < 0) playPosFrames = 0;
                play = false;

                // snap playhead to clip in out
                if (Form.ModifierKeys == Keys.Shift)
                {
                    int closest = snapPosList.OrderBy(n => Math.Abs(n - (playPosFrames ))).First();

                    int distance = Math.Abs(playPosFrames  - closest);

                    if (distance < 12) playPosFrames = closest;

                    //FLogger.Log(LogType.Debug, distance.ToString());
                }
                //Invalidate(); //redraw
            }
        }

        public void UpdateSnapList()
        {
            snapPosList.Clear();
            List<Clip> snapClipList = ipD.ClipList.Where(x => x.Bounds.IntersectsWith(myworkspace.Bounds)).ToList();

            //snapPosList.Add(FLoopRegion[0] / timeZoom);
            //snapPosList.Add(FLoopRegion[1] / timeZoom);

            /*
            foreach (int pos in FMarker)
            {
                snapPosList.Add(pos / timeZoom);
            }
            */

            foreach (Clip myclip in snapClipList)
            {
                snapPosList.Add(myclip.Bounds.X);
                snapPosList.Add(myclip.Bounds.X + myclip.Bounds.Width);
                if (myclip.FadeIn > 0) snapPosList.Add(myclip.Bounds.X + (myclip.FadeIn));
                if (myclip.FadeOut > 0) snapPosList.Add(myclip.Bounds.X + myclip.Bounds.Width - (myclip.FadeOut));
            }
            //FLogger.Log(LogType.Debug, snapPosList.Count.ToString());
        }

        public void SetSelectionRectangle(System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && hitObject is WorkSpace)
            {
                //Size targetSize = new Size(mousePos.X - holdMousePos.X, mousePos.Y - holdMousePos.Y);

                //negative and positive rectangle since drawing doesnt work with negative values
                var rc = new Rectangle(
                                        Math.Min(holdMousePos.X, mousePos.X),
                                        Math.Min(holdMousePos.Y, mousePos.Y),
                                        Math.Abs(mousePos.X - holdMousePos.X),
                                        Math.Abs(mousePos.Y - holdMousePos.Y));

                selectionRectangle = rc;        //new Rectangle(holdMousePos, targetSize);
                MultiSelectionClips();
                //Invalidate();
            }
        }

    }
}
