using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace VVVV.Nodes
{
    partial class Interpole
    {

        public Point prevMousePos; //previous Mouse Pos

        private void Form1_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            prevMousePos = MousePosition; // for delta calculation below



        }

        private void Form1_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {


        }

        private void Form1_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            int x = Convert.ToInt32((e.X - StagePos.X) / dpi);
            int y = Convert.ToInt32((e.Y - StagePos.Y) / dpi);

            string input = "MyClip";
            int frames = 0;
            if (ClipDialog(ref input, ref frames, "Add State", dpi) == DialogResult.OK)
            {
                //add state to state list
                clipList.Add(new Clip()
                {
                    ID = RNGCharacterMask(),
                    Name = input,
                    StartFrame = frames,
                    Bounds = new Rectangle(new Point(x , y ), new Size(300, 30))
                });

               
            }
            
        }

        private void Form1_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            CalcStagePosition(MousePosition, e.Button == MouseButtons.Right); // calculate delta on rightclick
               
            

            this.Invalidate(); //redraw
        }


        public void CalcStagePosition(Point mousePos, bool click)
        {
            if (click)
            {
                int deltaX = mousePos.X - prevMousePos.X;
                prevMousePos = mousePos;
                StagePos.X += deltaX;
                if (StagePos.X > 0) StagePos.X = 0;         
            }
        }


    }
}
