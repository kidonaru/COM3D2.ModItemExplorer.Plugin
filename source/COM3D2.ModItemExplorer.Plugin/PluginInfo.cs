using System;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    internal static class PluginInfo
    {
        public const string PluginName = "ModItemExplorer";
        public const string PluginFullName = "COM3D2." + PluginName + ".Plugin";
        public const string PluginVersion = "1.4.0.0";
        public const string WindowName = PluginName + " " + PluginVersion;

        public readonly static byte[] Icon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAD" +
            "CUlEQVRYhe2XTyh0axjAf2cOZkxxGoxRmiQWs0BiJYnCxkaKDUomKXbKCgsKycaKspCdWLBQSklo" +
            "ippTUjNOhsiZkpQ0mKaDzPk2955mYu53x/VdFp7d+z7/fud9n+d5O4Ku6zpfKKavTP4D8C0AUmIX" +
            "19fXhEKhhMaCIJCbm0tWVtanAQh/d8Hj4yNbW1u/dxAEKioqKCgo+BQA4wqenp7+lYOu6xweHnJ8" +
            "fMxndPCHakDXdQKBAH6//z8DxF2BoihJB7BaraSkpPze8C9JTU3F6XRiNpuBmCIURZHMzMykAZKV" +
            "l5cXQqEQDocD+KI2jK2d7zUHADweD5FIBJfLFddqd3d3yLIMQGNjIyZTcuyqqnJycoLL5cJmsyUG" +
            "mJiYIBgMUlVVxcLCgrE/NzfH0tISAEdHR6SlpSUF4PF4mJycZGhoiPLycmM/4WccHBzg8/kAuLy8" +
            "ZG1t7Y1NNBrl4uICWZZRVfWNPhwOI8syV1dXCcES9k9RURErKyuUlpYyOzuL3W7n4eGB+/t7AJ6f" +
            "n+nt7cXr9Ro+JSUlhu3Z2RldXV3GaI899lhJeAJut5vt7W3Oz8/Z3Nyku7sbURQN/eLiIl6vl/n5" +
            "eRRFYXx8HL/fz9jYGAAjIyOIosj6+jqKotDe3p4cQFNTE4Ig0NPTgyRJNDc3x+l3d3cpLCykpqYG" +
            "gJaWFrKzs9nb2yMYDOLz+airq6O4uBgASZKSAzCbzdTX13Nzc0Nrayvp6elxek3TsFqtxloQBHJy" +
            "cnh9feX29haAjIyMROEN+ccZ2tbWRjgcprOz843ObrcTCASIRqOYTCY0TUNVVSRJIj8/H8AA+TBA" +
            "WVkZMzMz7+o6Ojro6+tjdHSU6upqNjY20DQNt9uNw+GgsrKSnZ0dlpeXsVgsrK6uvhvnw5OwtraW" +
            "4eFh9vf3GRgY4PT0lMHBQfr7+wGYmprC6XQyPT2N1+uloaHh3TjGaxiJRN7t5T8hNpuNvLw84Bu8" +
            "BQaAIAj/W9LYXEYRWiwWJElC07Q/mlwUxbipKPz8mv0AfDXAL2rnH6FI5QchAAAAAElFTkSuQmCC");

        public readonly static byte[] SearchIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAB" +
            "2klEQVRYhe2XMS9DURTH/5oaxMBgYxMshsYiERtGo4RETL6BSHwBgy9gFAYJBhMGo4iBgbAxIQaD" +
            "EBIaafMzvPs4nrbvvdtqDf7JzTt5Pfee3z2359y2CVAjlWlo9H+AvwCQ9ZiTkzQuqVtSXtKxpG1J" +
            "T14EQJKRAWaAU0rrFVgFehKu9zmSOI0A52UCR/UOLANttQIYAYomwC0wB/S5rLQCY8BaxO8CaK8W" +
            "IBPZ+ZoLWM5/ELgz/lvVAsxEgidJaQ/waOblqgE4NWmvtPNK4Iu+ADmzyFyK4AKywLWbexDnX64R" +
            "jRt7J2VlFyTtObs3zrkcQLexr1ICSNKle3b4AuSN3eIBEM4p+AKcGHvIA2DYPW98AbYkvTl7OmXw" +
            "Lkmjzj7zBXiRtGkABlMALElqdvZ+rHeFEukj6O0QdLgkF82CKd8HErTjuAWXzYKPrslkS/h1Aet8" +
            "10qSvtEEFX+Utkk6lNRv3t1I2lVQni0KvnCjJu1WU5I2fI8gHO0EF0sSPbidh3oGBqo5gmh7XiRo" +
            "r/dAAcgDlw5wlq8zn3TBQ4jJWgCkHQMGAmC+3gBhJqx+QPw2gFzQUEVgot4AIUSR4JrutJ/FlWEt" +
            "NSHpSNKdfVlPgJJq+D+jhgN8AIT9K5FUTDXPAAAAAElFTkSuQmCC");

        private static Texture2D _searchIconTexture = null;
        public static Texture2D SearchIconTexture
        {
            get
            {
                if (_searchIconTexture == null)
                {
                    _searchIconTexture = new Texture2D(1, 1);
                    _searchIconTexture.LoadImage(SearchIcon);
                }
                return _searchIconTexture;
            }
        }

        public readonly static byte[] OpenIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAA" +
            "k0lEQVRYhe3XsQ2AIBAF0K9xAFuYwhF0AzcwbuYGjoAjMMXRugFWdkBETTDxX8mR/BcukFB571Gy" +      
            "6qLpBBBAAIAm1nDO9QBMpL0DGLTWNics9OakTqBP9FoARkS6HEConozgRIxPANERZCBWEbmydwaw" +      
            "XAYope6iYjWFAMVvAQEEEEDApwHby1k2tFjxX0AAAb8HHCkbHk3ntkRnAAAAAElFTkSuQmCC");

        private static Texture2D _openIconTexture = null;
        public static Texture2D OpenIconTexture
        {
            get
            {
                if (_openIconTexture == null)
                {
                    _openIconTexture = new Texture2D(1, 1);
                    _openIconTexture.LoadImage(OpenIcon);
                }
                return _openIconTexture;
            }
        }

        public readonly static byte[] UpdateIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAB" +
            "9UlEQVRYhe2XPUucQRDHf3ea+IIBcxzcqWAhhEACIRcUv4CF6axiYaGN38BSxD6pktYmRe4jhBCw" +
            "CdgYwcNCELGx0CakCkYU/Vvsyi2L++JziCT4h+HmmZtnZm7nba8kiftE+V69PwTwPwRQsRTCc2As" +
            "ZqC7oOMGMA4MAQKOgJ/Ajqe3CHyJWpJ0G1qQ1FIYm5JmrO6aldVjNnMd90tajzj2sWM/LyVVYrZL" +
            "GYPoMbAPjDqyU+AbsGufXwJvra6Lc2AY+NVJCja8X/dB0mBA98DTPZdU7SQF7zyD8wG9AUnNQDpq" +
            "MR+pLnjv8J+AzwG9N/aom8ClI78ATmIOYjXwGti2/CnQF4+1GGKDaMrhv9+Fc4gPoqcO3ypguwz0" +
            "WP4COLtRK1IgA5KGLPVldItPy5JOLK0WKcI/lopiknbdBAsxZxAVxV+g1/INAmlMteETTPt1ObIy" +
            "MAKsAD8C7310nB+GnAPJQVQLDJemrZGb3pn3dGdjPlIBVGXGqYuDgO6gzJh2sZEq1lQNVDG7/pEn" +
            "PwO+0l5GL4Bp59ivj/4ZofbLTEFFZqVK7RWbg3WZFZ5s15RC3Rpcs88zMpeOEFoyl5bseZFKQQOY" +
            "A5Y8+StgArOASsAxsEV7d2QjFcAYJv97ge+vL6S/b+s4N4A7xz//v+AhgI5xBdHP+RxXlRauAAAA" +
            "AElFTkSuQmCC");
        
        private static Texture2D _updateIconTexture = null;
        public static Texture2D UpdateIconTexture
        {
            get
            {
                if (_updateIconTexture == null)
                {
                    _updateIconTexture = new Texture2D(1, 1);
                    _updateIconTexture.LoadImage(UpdateIcon);
                }
                return _updateIconTexture;
            }
        }

        public readonly static byte[] ExpandIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAA" +
            "q0lEQVRYhe3VUQqDMBAE0JnSA+SK3iA38kgeafpRhRa6k1RTFLoDAT/izosoUhLOzO3U9gQkIAEJ" +      
            "uDyAJAAUAJUkvlkAKoCyXseR5FaRtOiZufdUkub1nmWdEXa0AFXvaSJeyrfU3YBgYIiI9h4C9CLc" +      
            "nsOAjgILHAKIijqfzhhAgLDlwwEG4V7OsYAPCPt5uo67bTEhOW1IktPuOa2T/jrX/hklIAEJ+AvA" +      
            "A6NYpXDztRgWAAAAAElFTkSuQmCC");

        private static Texture2D _expandIconTexture = null;
        public static Texture2D ExpandIconTexture
        {
            get
            {
                if (_expandIconTexture == null)
                {
                    _expandIconTexture = new Texture2D(1, 1);
                    _expandIconTexture.LoadImage(ExpandIcon);
                }
                return _expandIconTexture;
            }
        }

        public readonly static byte[] CollapseIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAA" +
            "lElEQVRYhe3VQQrAIAwEwE1f4FP9UZ/UJ20PtYcejBEag5AFr7sDggpJROYIXU9AArYDiAgAFABV" +      
            "RKAdc0jOnELy4pPzj95ZQOU3XYQLoBWfFoQbwIpwBVgQ7oARYglAQywD9BDWzvCXcO8r6I0vAWjj" +      
            "7oDRuCvAMu4GYPRnxOjvuBUXklUbnwHIWxyV8JcwAQkIB9w1tg9Jmmkd5AAAAABJRU5ErkJggg=="); 
        
        private static Texture2D _collapseIconTexture = null;
        public static Texture2D CollapseIconTexture
        {
            get
            {
                if (_collapseIconTexture == null)
                {
                    _collapseIconTexture = new Texture2D(1, 1);
                    _collapseIconTexture.LoadImage(CollapseIcon);
                }
                return _collapseIconTexture;
            }
        }

        public readonly static byte[] ListIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAA" +
            "5ElEQVRYhe2UIQoCYRSEvxUtgrhFBLvJc3gAz+ApBPEIFpPYbRZPoDewWDbZTBaLCrLPsAi2nbes" +
            "/OUf2PaxM/DPvMTMCKlGUPcYIAYAmg52BPQc/EGizEz5tmb2Np+Oyr8T8Q5UPRZJGaB24FnB/KVA" +
            "agcmwALoinwOrBVQfYK/KfgMgwdQOzAGZkBf5B/ABqEHagfuQEc0/w3RritA8DuwpJiWR3sF8sww" +
            "BVqOADeE0PEOqDNMgSn6EnJgB5zLQPUJMmAomn91BQZ1BQg+w1MF84sCeU7xnKILqlYKFGcYA8QA" +
            "H/swpNG8yEz+AAAAAElFTkSuQmCC");

        private static Texture2D _listIconTexture = null;
        public static Texture2D ListIconTexture
        {
            get
            {
                if (_listIconTexture == null)
                {
                    _listIconTexture = new Texture2D(1, 1);
                    _listIconTexture.LoadImage(ListIcon);
                }
                return _listIconTexture;
            }
        }

        public readonly static byte[] FavoriteOffIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAB" +
            "w0lEQVRYhe2X4VHDMAyFn7kOkA0aNvAGhA06QpkANqBMAJ0gbAAbBCZINmiZoNng8SNyqwYnsdtc" +
            "8gfd+Zzalt4X1XJdQxJz2s2s6v8AABb6gzEmyplkIX73kX7nH1yLDJLxZFkswFHzCoBCARSTApC0" +
            "IryTRpJ2SoBcRNfSSDKfBIBk6t5ejbkspLEAl5Tho/RbNeaen2ODGf3mXWVIMgFgASQAXKpvjTG1" +
            "mncZeQBQA6jcvCfe8XnRmrAAVgCWAFIRtJ4YLzq4MaYmuUWTgY+WUCVAewA/AD5l7C+AiLfT+CX9" +
            "t/SVGtP2JnMO+E76zLP2CHC2IUgmJEvZUKWk9irzxeytgjEhumINluEYEH0xgs6BayCGfIMA1OLc" +
            "BYoAcOLe0zEKQAeMAOgF1pqhJ2GKpo5DbS8+gzYIIN9fcgFAErJvQjJgpf/Wg7LRnqS1hdxaiwGL" +
            "AaiV+BpACeBVWiljzvZjAiylr9hcw0o0P0gpgHdpKYBcNmumAJYYsoAyLGRXu949W7XGeubJjqta" +
            "7DlwUIF3JFc9L7Pi6XJCkocxACgQmy5hj8/GgY8BkIaUk8cvYccVTWuaLuGpbPa/ZrMD/AL/kRDG" +
            "XW/pCgAAAABJRU5ErkJggg==");

        private static Texture2D _favoriteOffIconTexture = null;
        public static Texture2D FavoriteOffIconTexture
        {
            get
            {
                if (_favoriteOffIconTexture == null)
                {
                    _favoriteOffIconTexture = new Texture2D(1, 1);
                    _favoriteOffIconTexture.LoadImage(FavoriteOffIcon);
                }
                return _favoriteOffIconTexture;
            }
        }

        public readonly static byte[] FavoriteOnIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAC" +
            "kUlEQVRYhe2XPUwUQRiGn0EEf068xAQNJnqhgBBjxBjFEjsLCzFB27vG2ElEEyuBzsJESqMmR0tF" +
            "4W9swFiZGLSxsNA7QYzEEDi5Azm8fS12jvuDY/fuAg1fMpmdzHzf8+43MzuzRhLbaXXbSt8RANTn" +
            "N4wxvpwlha3fiE+/wka2+AwSlDRvS9CvgGypZgr6UDKIkkGgr+IoVWQgptSolByVpFjFzEoESApL" +
            "kn5dkX5elrXwVgqIKfVCmu6Svp+VFp/5ykJVa0BSNxAi8QQcBzIOzD0CCNk+X+ZLgF3tA6xMwuqM" +
            "C3ccSP+ApQ8AA353hMlPvTEm94ZuOQh0AkFbuzZ7wwU6Ti4Le85AKJof+xOwYOsEEAfixpiJAmbR" +
            "3HcD4ygJma8gQfqjW69MurCVL7CaKIQ7DjgZIACN7W47cM71O3Dejbz/BNQ3AVwAJsplIAqESd6H" +
            "5ZdFkI2eM5CRrdfpb+6F9mGAEWNMJJ9ZsgaMMRFgiMBdaLhYS/iQjV3IK85A1uy+jvLnKSw8rgx+" +
            "7BYc7weI5J8XG66B4sNoTUTyOcze8wdvewiHr5bAiwWU3YbWMULgEhzq9w5vHdwQXmybfgdsgGEa" +
            "2rzBHcdd8TDs5Zj2+iEKkZ7xBs84sDzl+ngwfwK8wB0HlmovoJPF96XwvR2wr6NUyNw716cWAiS5" +
            "gf5O5SC7W6D1AZx6DaffuPu88agVkoFkvNC3GgFkU7k8DXUBaLkJJ19Bc28c6AF6OHItTtc4tN6B" +
            "XU2QihX6bvKGZe8DkgaV+ix9uy39S8jeAQfXHSfNK70gTV6Xfr/VeuNKmB4EjCln0XLHrb2oRvPG" +
            "j9VCQFTSuJf5zPPptD7RDfrXSvFxvOW27X9GOwL+A3Iuz0y+jzwNAAAAAElFTkSuQmCC");

        private static Texture2D _favoriteOnIconTexture = null;
        public static Texture2D FavoriteOnIconTexture
        {
            get
            {
                if (_favoriteOnIconTexture == null)
                {
                    _favoriteOnIconTexture = new Texture2D(1, 1);
                    _favoriteOnIconTexture.LoadImage(FavoriteOnIcon);
                }
                return _favoriteOnIconTexture;
            }
        }

        public readonly static byte[] SortDefaultAscIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAB" +
            "E0lEQVRYhe1W2w3CMAw8IwboCIzACGUT2AAmoEwAI3QDRoANYARG6AbHBxFN3BpSqS9QT4qanuz4" +
            "1Di5CkkMidmg1ScBkwAAc4NPAGzd/AEgj11QRN5zkpnjMo8LE0jWjTNDrI24ylBrk4rU8dYWJOp9" +
            "EfsFmmK0PXDw5gWAU1cCpO2rWDUhHSceF8QPvgWjFXAEwA8j7VrA8kte5wJ6gyXg/iXv2paA6Ri2" +
            "LoDkTZuQZ2o3HW9tQQpgH1mzALBxT+B1gi6oGloBYAXdX4atXtgMmW/Hzr411nX1OukBEckR/sTk" +
            "jqvAcsOiYc1HDbdDeaHtrESrBxKUv2QxxfP3guExXDru7nFRAnrD/90Dk4CfE/AEPIsfdUBobsUA" +
            "AAAASUVORK5CYII=");

        private static Texture2D _sortDefaultAscIconTexture = null;
        public static Texture2D SortDefaultAscIconTexture
        {
            get
            {
                if (_sortDefaultAscIconTexture == null)
                {
                    _sortDefaultAscIconTexture = new Texture2D(1, 1);
                    _sortDefaultAscIconTexture.LoadImage(SortDefaultAscIcon);
                }
                return _sortDefaultAscIconTexture;
            }
        }

        public readonly static byte[] SortDefaultDescIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAB" +
            "D0lEQVRYhe1W2xGCMBDcOBZACZZgCdgJdmAHagWUgB1YAnYAJVACHaw/pxNCDoIDhg925oaw3GOH" +
            "XG5iSCImdlGrbwLWIGA/d0JjzHdN8ihcbXFdf+UUJAAusm4APKYKIJkAKIU+GWNanwCQ9NmTXWSK" +
            "X8+svIUVX2j1tB5InPdD6B+QIhmAzKIy4XqYvQll33PPp/zTE26Az1KSpdiTZBK6BSQr6qhcf60J" +
            "f4ZzCiicsbiOf/Q5sFoBOQAOWLq0gONI3OIC/gZNQD0S95pLwHYMowvQ7gMpgGtgjhbAWZ7Toczz" +
            "cmCe+3D7xDq5SYd0a612C9qJeRqFv48FhlzJQoo/An2DBfwN0XtgE7AJeAO3N3IABO7FNAAAAABJ" +
            "RU5ErkJggg==");

        private static Texture2D _sortDefaultDescIconTexture = null;
        public static Texture2D SortDefaultDescIconTexture
        {
            get
            {
                if (_sortDefaultDescIconTexture == null)
                {
                    _sortDefaultDescIconTexture = new Texture2D(1, 1);
                    _sortDefaultDescIconTexture.LoadImage(SortDefaultDescIcon);
                }
                return _sortDefaultDescIconTexture;
            }
        }

        public readonly static byte[] SortNameAscIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAB" +
            "PElEQVRYhe2WzW3CQBCF36AUkG2BCiyRBiLRAndOtOAr11TB3WkhUhoAkQbSgqGDx2UM44n/iP8E" +
            "8kgjed8Ou9887yILSYwZs1F3nwAmAAAvXS8oItdnklvVtkbL13d9DR0AVROj5epHfwUPBZACoGY8" +
            "NMAKQHDjQQGWbrzQHAQg4Nbxh9G7cYFkXW54iwXJRJ/Tonq3NulEX9/EgazTg+aXjq0z/4+a7uem" +
            "+1i1YLSkrQN1ALGzP9MTo4c+Afasj00bgKoz0PSqtToHVQB24TcA4vJT55YA5lkhyaPv3Lh1/LNL" +
            "hf2p/mhfMm+vZ2z0iOSp4FWddK7RGViVLG4zGMhfewZIrgsA1kUNN/kjuiuNszuz+a7M8d4+SEi+" +
            "AvhW+V1EzhlArr4vAN0sUu3HaP0C3BsP9UU0ATwnwAXJes1ji+FkZwAAAABJRU5ErkJggg==");

        private static Texture2D _sortNameAscIconTexture = null;
        public static Texture2D SortNameAscIconTexture
        {
            get
            {
                if (_sortNameAscIconTexture == null)
                {
                    _sortNameAscIconTexture = new Texture2D(1, 1);
                    _sortNameAscIconTexture.LoadImage(SortNameAscIcon);
                }
                return _sortNameAscIconTexture;
            }
        }

        public readonly static byte[] SortNameDescIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAB" +
            "OklEQVRYhe2WzQ3CMAyFbcQAdAUmQIIFkFihd05doWeOTMG9rIDEAiBYgBWADR4XB4zpH2lKOfRJ" +
            "UdNXK/niumkYAHWpQaez9wD/ADAMPSAzP/sAJuKdlfceH/orcAAARkS0F3vOzPc8AAIQtKlxN3hp" +
            "UzRfKwAAlvjU8icAACYAbjkAN3nmDXBVg6UlcaecyZ1OvgCxGehQFGvqCzCmja+7DyzM/VRaY9UB" +
            "iIgolv5a+XFO7Peqkf5EpX4KIJP+9VevwK30KG0n9zoz/qpY/Vit3lV+pLysaQaqAFKTfudnyo/a" +
            "BDigWkkTgLIaqPupNaqDMgA98IyI2LStPFsQ0bgNgESurvqtdqrvn4WCd6+33qJ9P8Lr/3DxrYHW" +
            "DiQOQDxW3lt88COZ0aoqIHgGvlXnp+IeoAfoHOABNCKz42myiaMAAAAASUVORK5CYII=");

        private static Texture2D _sortNameDescIconTexture = null;
        public static Texture2D SortNameDescIconTexture
        {
            get
            {
                if (_sortNameDescIconTexture == null)
                {
                    _sortNameDescIconTexture = new Texture2D(1, 1);
                    _sortNameDescIconTexture.LoadImage(SortNameDescIcon);
                }
                return _sortNameDescIconTexture;
            }
        }

        public readonly static byte[] SortTimeAscIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAB" +
            "VUlEQVRYhe2W4W2DMBCFn6sOwAiMwAqdAEZghIzAJu4IZQKaCdwRukGyweuPnpvDMQ4QI1SJJ50g" +
            "x8P+YvtOGJLYUy+7zn4AHAAAXnMPaIz5uyfZSa5TubE/dxkGAJScUbmR/19tQQOgBlDK728APYCP" +
            "pwhIPoqKpONNg4SXE8/d8npDar45k18kTiQLko1EITn/vNoCwOnBJfwKhJAuN0Aj75+CfAgA8ZBk" +
            "sxQgVQW1XN8THi/vqVOmmFJVUAL4BHCNPKsADHJ/BtCJt8wJMKXzinemlTgDVraweHBQIR6StMHY" +
            "T52BXq7tjP/hPX3KFNWKMpzqFdnLcKoR6WW/a0Qcd81QbimAh9CDDky0YgUd6sLISs4B0I3JKgAr" +
            "ufGAv8vcRgDaNVuwONReWzW5nToDm32QkCxwa1ZvxpirBxj5twKQySrJfanctgBLtfsn2QFwAPwA" +
            "c0nIHehOUa0AAAAASUVORK5CYII=");

        private static Texture2D _sortTimeAscIconTexture = null;
        public static Texture2D SortTimeAscIconTexture
        {
            get
            {
                if (_sortTimeAscIconTexture == null)
                {
                    _sortTimeAscIconTexture = new Texture2D(1, 1);
                    _sortTimeAscIconTexture.LoadImage(SortTimeAscIcon);
                }
                return _sortTimeAscIconTexture;
            }
        }

        public readonly static byte[] SortTimeDescIcon = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAC4jAAAuIwF4pT92AAAB" +
            "V0lEQVRYhe1W0XWEIBAc8lIAJVgCLVwFWoIlXAlJJaaEWIG5CrgS0kGug8lHlpPbKB4XfObDeW8f" +
            "ug7LsMCKIYkt8bTp6LuA/yDguXRAY8z1maQT3zny3fJLn4IggKQFMIj7YIy5TAkAyaIWxe04opsb" +
            "bxUBJFv+RjslIGcJGgA1gErePwH0AN4Vz0nqrfJfABwAnHOXwJH00UwGsQAvnMCPuRo+dwkcyS+x" +
            "I0lLshGz4gvfnc5mGDU14SUBPg4uFjKgRfrSAhrpf1R+LQDCIckmV0CqEtbSviU4AYFTp0hTSFXC" +
            "CsAHfnavhsNYZE4AXoRblRQwh9MDfeaR2AOdLKFd2KgQDhlVPIn9pz3QS9veMY/A6VOkSTxwDOdq" +
            "RfFjOFeI4rSvXoiCCM8RAxOlOFdA8Z+RupBQfCby3QRd7UJyr4DiVzKF1yVC8QzkYvNb8S5gF7C5" +
            "gG+je7gULprntwAAAABJRU5ErkJggg==");

        private static Texture2D _sortTimeDescIconTexture = null;
        public static Texture2D SortTimeDescIconTexture
        {
            get
            {
                if (_sortTimeDescIconTexture == null)
                {
                    _sortTimeDescIconTexture = new Texture2D(1, 1);
                    _sortTimeDescIconTexture.LoadImage(SortTimeDescIcon);
                }
                return _sortTimeDescIconTexture;
            }
        }
    }
}