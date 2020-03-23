#region License & Metadata

// The MIT License (MIT)
// 
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
// 
// 
// Modified On:  2020/03/20 15:58
// Modified By:  Alexis

#endregion




using System.Collections.Generic;
using System.Linq;

namespace Squirrel.Tests.TestHelpers
{
  public static class TupleExtensions
  {
    #region Methods

    public static void Deconstruct<T>(
      this IList<T> list,
      out  T        t1)
    {
      t1 = list.Count > 0 ? list[0] : default(T); // or throw
    }

    public static void Deconstruct<T>(
      this IList<T> list,
      out  T        t1,
      out  T        t2)
    {
      t1 = list.Count > 0 ? list[0] : default(T); // or throw
      t2 = list.Count > 1 ? list[1] : default(T); // or throw
    }

    public static void Deconstruct<T>(
      this IList<T> list,
      out  T        t1,
      out  T        t2,
      out  T        t3)
    {
      t1 = list.Count > 0 ? list[0] : default(T); // or throw
      t2 = list.Count > 1 ? list[1] : default(T); // or throw
      t3 = list.Count > 2 ? list[2] : default(T); // or throw
    }

    public static void Deconstruct<T>(
      this IList<T> list,
      out  T        t1,
      out  T        t2,
      out  T        t3,
      out  T        t4)
    {
      t1 = list.Count > 0 ? list[0] : default(T); // or throw
      t2 = list.Count > 1 ? list[1] : default(T); // or throw
      t3 = list.Count > 2 ? list[2] : default(T); // or throw
      t4 = list.Count > 3 ? list[3] : default(T); // or throw
    }

    public static void Deconstruct<T>(
      this IList<T> list,
      out  T        t1,
      out  T        t2,
      out  T        t3,
      out  T        t4,
      out  T        t5)
    {
      t1 = list.Count > 0 ? list[0] : default(T); // or throw
      t2 = list.Count > 1 ? list[1] : default(T); // or throw
      t3 = list.Count > 2 ? list[2] : default(T); // or throw
      t4 = list.Count > 3 ? list[3] : default(T); // or throw
      t5 = list.Count > 4 ? list[4] : default(T); // or throw
    }

    #endregion
  }
}
