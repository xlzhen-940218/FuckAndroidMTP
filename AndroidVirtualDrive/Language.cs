using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AndroidVirtualDrive
{
    public enum Lang { ZH, EN, Bilingual }
    public class Language
    {
        public static Lang currentLang = Lang.ZH;

        // 多语言输出辅助方法
        public static string L(string zh, string en)
        {
            if (currentLang == Lang.ZH) return zh;
            if (currentLang == Lang.EN) return en;
            return $"{zh}\n{en}";
        }
    }
}
