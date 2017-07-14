using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace VVVV.Nodes
{
    partial class Clips
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
            //x = Convert.ToInt32((e.X - p.StagePos.X) / p.dpi);
            //y = Convert.ToInt32((e.Y - p.StagePos.Y) / p.dpi);

            /*
            if (ClipDialog(ref input, ref frames, "Add State", p.dpi) == DialogResult.OK)
            {
                //add state to state list
                stateList.Add(new State()
                {
                    ID = Automata.Data.State.RNGCharacterMask(),
                    Name = input,
                    Frames = frames,
                    Bounds = new Rectangle(new Point(x - (p.StateSize / 2), y - (p.StateSize / 2)), new Size(p.StateSize, p.StateSize))
                });

                UpdateStateConfigs(); // update JSON,Enums and Redraw
                UpdateOutputs();
            }

            */
        }

        private void Form1_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            TranslateStage(MousePosition, e.Button == MouseButtons.Right); // calculate delta on rightclick
               
            

            this.Invalidate(); //redraw
        }


        public void TranslateStage(Point mousePos, bool click)
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
