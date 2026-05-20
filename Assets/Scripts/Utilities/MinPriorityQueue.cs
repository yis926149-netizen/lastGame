using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 自定义最小优先队列（按优先级升序出队）
/// 适配：KeyValuePair<Vector3, float> 数据类型（点 + 到达代价），优先级为到达代价
/// 
/// 公共方法
/// 1、构造函数：public MinPriorityQueue()
/// 2、入队方法：public void Enqueue(KeyValuePair<Vector3, float> data, float priority)
/// 3、出队方法：public KeyValuePair<Vector3, float> Dequeue()
/// 4、清空队列：public void Clear()
/// </summary>
public class MinPriorityQueue
{
    // 堆节点结构体：存储数据和优先级
    private struct HeapNode
    {
        public KeyValuePair<Vector3, float> Data; // 数据：(点, 到达该点的代价)
        public float Priority; // 优先级：与到达代价一致（升序）

        public HeapNode(KeyValuePair<Vector3, float> data, float priority)
        {
            Data = data;
            Priority = priority;
        }
    }

    private readonly List<HeapNode> _heap; // 底层存储：List实现堆结构
    private readonly IComparer<float> _comparer; // 优先级比较器（最小堆）

    /// <summary>
    /// 队列元素个数
    /// </summary>
    public int Count => _heap.Count;

    /// <summary>
    /// 队列是否为空
    /// </summary>
    public bool IsEmpty => _heap.Count == 0;

    /// <summary>
    /// 构造函数：默认最小堆（优先级升序）
    /// </summary>
    public MinPriorityQueue()
    {
        _heap = new List<HeapNode>();
        // 最小堆比较器：a.Priority < b.Priority 时，a 应排在前面
        _comparer = Comparer<float>.Create((a, b) => a.CompareTo(b));
    }

    /// <summary>
    /// 入队：添加元素到堆，并上浮调整堆结构
    /// </summary>
    /// <param name="data">数据：KeyValuePair<Vector3, float>（点 + 到达代价）</param>
    /// <param name="priority">优先级（必须与到达代价一致，算法中已保证）</param>
    public void Enqueue(KeyValuePair<Vector3, float> data, float priority)
    {
        // 添加到堆尾
        _heap.Add(new HeapNode(data, priority));
        // 上浮调整：从最后一个节点向上维护堆结构
        UpHeapify(_heap.Count - 1);
    }

    /// <summary>
    /// 出队：移除并返回优先级最高（最小）的元素，下沉调整堆结构
    /// </summary>
    /// <returns>优先级最高的元素</returns>
    /// <exception cref="InvalidOperationException">队列为空时抛出</exception>
    public KeyValuePair<Vector3, float> Dequeue()
    {
        if (IsEmpty)
            throw new InvalidOperationException("优先队列为空，无法执行出队操作");

        // 取出堆顶（优先级最高）元素
        HeapNode topNode = _heap[0];
        // 用堆尾元素替换堆顶，然后移除堆尾
        int lastIndex = _heap.Count - 1;
        _heap[0] = _heap[lastIndex];
        _heap.RemoveAt(lastIndex);

        // 下沉调整：从堆顶向下维护堆结构
        if (_heap.Count > 0)
            DownHeapify(0);

        return topNode.Data;
    }

    /// <summary>
    /// 上浮调整：从指定索引向上，确保父节点优先级 ≤ 子节点
    /// </summary>
    private void UpHeapify(int index)
    {
        while (index > 0)
        {
            int parentIndex = (index - 1) / 2; // 父节点索引

            // 若当前节点优先级 ≥ 父节点，堆结构已合法，退出
            if (_comparer.Compare(_heap[index].Priority, _heap[parentIndex].Priority) >= 0)
                break;

            // 交换当前节点与父节点
            Swap(index, parentIndex);
            // 继续向上调整
            index = parentIndex;
        }
    }

    /// <summary>
    /// 下沉调整：从指定索引向下，确保父节点优先级 ≤ 子节点
    /// </summary>
    private void DownHeapify(int index)
    {
        int heapSize = _heap.Count;

        while (true)
        {
            int leftChildIndex = 2 * index + 1; // 左子节点索引
            int rightChildIndex = 2 * index + 2; // 右子节点索引
            int smallestChildIndex = index; // 优先级最小的子节点索引

            // 找到左、右子节点中优先级最小的
            if (leftChildIndex < heapSize &&
                _comparer.Compare(_heap[leftChildIndex].Priority, _heap[smallestChildIndex].Priority) < 0)
            {
                smallestChildIndex = leftChildIndex;
            }

            if (rightChildIndex < heapSize &&
                _comparer.Compare(_heap[rightChildIndex].Priority, _heap[smallestChildIndex].Priority) < 0)
            {
                smallestChildIndex = rightChildIndex;
            }

            // 若当前节点已是优先级最小，堆结构合法，退出
            if (smallestChildIndex == index)
                break;

            // 交换当前节点与优先级最小的子节点
            Swap(index, smallestChildIndex);
            // 继续向下调整
            index = smallestChildIndex;
        }
    }

    /// <summary>
    /// 交换堆中两个索引的元素
    /// </summary>
    private void Swap(int indexA, int indexB)
    {
        HeapNode temp = _heap[indexA];
        _heap[indexA] = _heap[indexB];
        _heap[indexB] = temp;
    }

    /// <summary>
    /// 清空队列（可选，方便复用）
    /// </summary>
    public void Clear()
    {
        _heap.Clear();
    }
}