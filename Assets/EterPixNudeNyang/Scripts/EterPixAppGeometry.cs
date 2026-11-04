using UdonSharp;
using UnityEngine;

namespace EterPixNudeNyang
{
    // コラージュ画像の切り出し・回転・大きさの計算(状態を持たない).
    // U#では static が使えないため、コンポーネントのメソッドとして持つ.
    // 式は docs/contracts/image-formats.md と、Pad の K-4(eterpix_pad.cs ApplyImageLayout)に合わせる.
    // Editor の自己テスト(EterPixAppGeometrySelfTest)が、この同じコードを直接呼んで検証する
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class EterPixAppGeometry : UdonSharpBehaviour
    {
        // コラージュは 32:27 で、2列×3行。各セルは 16:9
        public const float CellAspectW = 16f;
        public const float CellAspectH = 9f;

        // copo(1〜6, 左上から右へ)のセルの uvRect
        public Rect CellUv(int copo)
        {
            int index = Mathf.Clamp(copo, 1, 6) - 1;
            int col = index % 2;
            int row = index / 2;
            float cellW = 0.5f;
            float cellH = 1f / 3f;
            return new Rect(col * cellW, 1f - (row + 1) * cellH, cellW, cellH);
        }

        // coro が奇数(90°/270°)の写真は、縦写真(9:16)がセル(16:9)の中に横倒しで梱包されている
        public bool IsPortrait(int coro)
        {
            return (((coro % 4) + 4) % 4) % 2 == 1;
        }

        // 写真を表示する枠(frameW × frameH)に対して、RawImage を時計回りに何回(×90°)回すか(0〜3).
        // 基本は coro 回(写真を正立させる向き). 正立した写真の縦横が枠の縦横と食い違うときは、
        // さらに反時計回りに90°回して、長辺を枠の長辺に合わせる(Pad の K-4 と同じ向き.
        // 時計回りに足すと、縦写真(coro奇数)は上下逆さ(合計180°)になるため)
        public int DisplayTurns(int coro, float frameW, float frameH)
        {
            int turns = ((coro % 4) + 4) % 4;
            bool shownLandscape = turns % 2 == 0; // セルは16:9なので、偶数回転後は横長
            bool frameLandscape = frameW >= frameH;
            if (shownLandscape != frameLandscape) turns = (turns + 3) % 4;
            return turns;
        }

        // 回す前の向きでの RawImage の大きさ(セルの縦横比16:9を保ち、回した後に枠へ収まる最大)
        public Vector2 FitSize(float frameW, float frameH, int turns)
        {
            bool rotated = turns % 2 == 1;
            float fitW = rotated ? CellAspectH : CellAspectW;
            float fitH = rotated ? CellAspectW : CellAspectH;
            float scale = Mathf.Min(frameW / fitW, frameH / fitH);
            return new Vector2(CellAspectW * scale, CellAspectH * scale);
        }

        // 一覧用: セルの中央を 1:1 に切り取る uvRect(横写真は左右、縦写真は上下が切れる).
        // セルの中央の正方形は、写真の中央の正方形でもある(回転しても同じ)ので、coro には依存しない
        public Rect SquareCropUv(int copo)
        {
            Rect cell = CellUv(copo);
            float squareW = cell.width * (CellAspectH / CellAspectW);
            return new Rect(cell.x + (cell.width - squareW) * 0.5f, cell.y, squareW, cell.height);
        }
    }
}
