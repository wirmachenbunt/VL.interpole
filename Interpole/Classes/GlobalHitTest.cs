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
        List<UIelement> hitestList = new List<UIelement>();

        public void hittest()
        {
            hitestList.Clear();

            hitestList.Add(myplaybar);


        }

    }
}
