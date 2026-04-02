using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// BEHAVIOR TREE - Core Framework
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// Project: Shadow Protocol - Comparative Study of FSM vs Behavior Trees
/// 
/// PURPOSE:
/// Lightweight Behavior Tree implementation for comparative analysis with FSM.
/// Implements same enemy behaviors (Patrol, Chase, Search) using BT architecture.
/// 
/// ARCHITECTURE:
/// - BTNode: Base class for all nodes
/// - BTComposite: Selector, Sequence (has children)
/// - BTDecorator: Modifies child behavior
/// - BTLeaf: Action and Condition nodes (no children)
/// 
/// NODE TYPES:
/// ┌─────────────────────────────────────────────────────────────┐
/// │ COMPOSITE NODES (have multiple children)                    │
/// │  • Selector: Returns SUCCESS if ANY child succeeds (OR)     │
/// │  • Sequence: Returns SUCCESS if ALL children succeed (AND)  │
/// ├─────────────────────────────────────────────────────────────┤
/// │ DECORATOR NODES (have one child)                            │
/// │  • Inverter: Inverts child result                           │
/// │  • Repeater: Repeats child N times                          │
/// ├─────────────────────────────────────────────────────────────┤
/// │ LEAF NODES (no children)                                    │
/// │  • Condition: Checks a condition, returns SUCCESS/FAILURE   │
/// │  • Action: Performs action, returns SUCCESS/FAILURE/RUNNING │
/// └─────────────────────────────────────────────────────────────┘
/// 
/// REFERENCES:
/// - Isla, D. (2005) 'Handling Complexity in the Halo 2 AI', GDC
/// - Champandard, A.J. (2007) 'Understanding Behavior Trees'
/// - Millington & Funge (2009) AI for Games, Chapter 5
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
namespace BehaviorTree
{
    #region ═══════════════════ ENUMS ═══════════════════

    /// <summary>
    /// Result of a node's execution.
    /// </summary>
    public enum NodeState
    {
        RUNNING,    // Node is still executing
        SUCCESS,    // Node completed successfully
        FAILURE     // Node failed
    }

    #endregion

    #region ═══════════════════ BASE NODE ═══════════════════

    /// <summary>
    /// Base class for all Behavior Tree nodes.
    /// </summary>
    public abstract class BTNode
    {
        /// <summary>Current state of this node</summary>
        public NodeState State { get; protected set; } = NodeState.FAILURE;
        
        /// <summary>Node name for debugging</summary>
        public string Name { get; set; } = "Node";
        
        /// <summary>Parent node (null for root)</summary>
        public BTNode Parent { get; set; }
        
        /// <summary>Shared data between nodes</summary>
        protected Dictionary<string, object> blackboard;
        
        /// <summary>
        /// Execute this node.
        /// </summary>
        public abstract NodeState Evaluate();
        
        /// <summary>
        /// Reset node state (called when tree restarts).
        /// </summary>
        public virtual void Reset()
        {
            State = NodeState.FAILURE;
        }
        
        /// <summary>
        /// Set shared blackboard reference.
        /// </summary>
        public virtual void SetBlackboard(Dictionary<string, object> bb)
        {
            blackboard = bb;
        }
        
        /// <summary>
        /// Get data from blackboard.
        /// </summary>
        protected T GetData<T>(string key)
        {
            if (blackboard != null && blackboard.TryGetValue(key, out object value))
            {
                return (T)value;
            }
            return default(T);
        }
        
        /// <summary>
        /// Set data in blackboard.
        /// </summary>
        protected void SetData(string key, object value)
        {
            if (blackboard != null)
            {
                blackboard[key] = value;
            }
        }
        
        /// <summary>
        /// Check if data exists in blackboard.
        /// </summary>
        protected bool HasData(string key)
        {
            return blackboard != null && blackboard.ContainsKey(key);
        }
    }

    #endregion

    #region ═══════════════════ COMPOSITE NODES ═══════════════════

    /// <summary>
    /// Base class for composite nodes (nodes with children).
    /// </summary>
    public abstract class BTComposite : BTNode
    {
        protected List<BTNode> children = new List<BTNode>();
        protected int currentChildIndex = 0;
        
        public BTComposite(string name = "Composite")
        {
            Name = name;
        }
        
        /// <summary>
        /// Add child node.
        /// </summary>
        public BTComposite AddChild(BTNode child)
        {
            child.Parent = this;
            children.Add(child);
            return this;
        }
        
        /// <summary>
        /// Add multiple children.
        /// </summary>
        public BTComposite AddChildren(params BTNode[] nodes)
        {
            foreach (var node in nodes)
            {
                AddChild(node);
            }
            return this;
        }
        
        public override void Reset()
        {
            base.Reset();
            currentChildIndex = 0;
            foreach (var child in children)
            {
                child.Reset();
            }
        }
        
        public override void SetBlackboard(Dictionary<string, object> bb)
        {
            base.SetBlackboard(bb);
            foreach (var child in children)
            {
                child.SetBlackboard(bb);
            }
        }
        
        public List<BTNode> Children => children;
    }

    /// <summary>
    /// SELECTOR (OR node): Returns SUCCESS if ANY child succeeds.
    /// Tries children in order until one succeeds.
    /// 
    /// Use for: "Try these options until one works"
    /// Example: Chase OR Search OR Patrol
    /// </summary>
    public class Selector : BTComposite
    {
        public Selector(string name = "Selector") : base(name) { }
        
        public override NodeState Evaluate()
        {
            for (int i = currentChildIndex; i < children.Count; i++)
            {
                currentChildIndex = i;
                
                switch (children[i].Evaluate())
                {
                    case NodeState.SUCCESS:
                        currentChildIndex = 0;
                        State = NodeState.SUCCESS;
                        return State;
                        
                    case NodeState.RUNNING:
                        State = NodeState.RUNNING;
                        return State;
                        
                    case NodeState.FAILURE:
                        // Try next child
                        continue;
                }
            }
            
            // All children failed
            currentChildIndex = 0;
            State = NodeState.FAILURE;
            return State;
        }
    }

    /// <summary>
    /// SEQUENCE (AND node): Returns SUCCESS only if ALL children succeed.
    /// Executes children in order, stops on first failure.
    /// 
    /// Use for: "Do all these things in order"
    /// Example: CheckPlayerVisible AND MoveToPlayer AND Attack
    /// </summary>
    public class Sequence : BTComposite
    {
        public Sequence(string name = "Sequence") : base(name) { }
        
        public override NodeState Evaluate()
        {
            for (int i = currentChildIndex; i < children.Count; i++)
            {
                currentChildIndex = i;
                
                switch (children[i].Evaluate())
                {
                    case NodeState.SUCCESS:
                        // Continue to next child
                        continue;
                        
                    case NodeState.RUNNING:
                        State = NodeState.RUNNING;
                        return State;
                        
                    case NodeState.FAILURE:
                        currentChildIndex = 0;
                        State = NodeState.FAILURE;
                        return State;
                }
            }
            
            // All children succeeded
            currentChildIndex = 0;
            State = NodeState.SUCCESS;
            return State;
        }
    }

    #endregion

    #region ═══════════════════ DECORATOR NODES ═══════════════════

    /// <summary>
    /// Base class for decorator nodes (nodes with one child).
    /// </summary>
    public abstract class BTDecorator : BTNode
    {
        protected BTNode child;
        
        public BTDecorator(BTNode child, string name = "Decorator")
        {
            this.child = child;
            if (child != null) child.Parent = this;
            Name = name;
        }
        
        public override void Reset()
        {
            base.Reset();
            child?.Reset();
        }
        
        public override void SetBlackboard(Dictionary<string, object> bb)
        {
            base.SetBlackboard(bb);
            child?.SetBlackboard(bb);
        }
    }

    /// <summary>
    /// INVERTER: Inverts child result (SUCCESS ↔ FAILURE).
    /// RUNNING remains RUNNING.
    /// </summary>
    public class Inverter : BTDecorator
    {
        public Inverter(BTNode child) : base(child, "Inverter") { }
        
        public override NodeState Evaluate()
        {
            if (child == null)
            {
                State = NodeState.FAILURE;
                return State;
            }
            
            switch (child.Evaluate())
            {
                case NodeState.SUCCESS:
                    State = NodeState.FAILURE;
                    break;
                case NodeState.FAILURE:
                    State = NodeState.SUCCESS;
                    break;
                case NodeState.RUNNING:
                    State = NodeState.RUNNING;
                    break;
            }
            
            return State;
        }
    }

    /// <summary>
    /// SUCCEEDER: Always returns SUCCESS regardless of child result.
    /// Useful for optional behaviors.
    /// </summary>
    public class Succeeder : BTDecorator
    {
        public Succeeder(BTNode child) : base(child, "Succeeder") { }
        
        public override NodeState Evaluate()
        {
            child?.Evaluate();
            State = NodeState.SUCCESS;
            return State;
        }
    }

    /// <summary>
    /// REPEAT UNTIL FAIL: Repeats child until it fails.
    /// </summary>
    public class RepeatUntilFail : BTDecorator
    {
        public RepeatUntilFail(BTNode child) : base(child, "RepeatUntilFail") { }
        
        public override NodeState Evaluate()
        {
            if (child == null)
            {
                State = NodeState.FAILURE;
                return State;
            }
            
            NodeState childState = child.Evaluate();
            
            if (childState == NodeState.FAILURE)
            {
                State = NodeState.SUCCESS;
                return State;
            }
            
            State = NodeState.RUNNING;
            return State;
        }
    }

    #endregion

    #region ═══════════════════ LEAF NODES ═══════════════════

    /// <summary>
    /// Base class for condition nodes.
    /// Conditions check something and return SUCCESS or FAILURE (never RUNNING).
    /// </summary>
    public abstract class BTCondition : BTNode
    {
        public BTCondition(string name = "Condition")
        {
            Name = name;
        }
    }

    /// <summary>
    /// Base class for action nodes.
    /// Actions perform something and can return SUCCESS, FAILURE, or RUNNING.
    /// </summary>
    public abstract class BTAction : BTNode
    {
        public BTAction(string name = "Action")
        {
            Name = name;
        }
    }

    /// <summary>
    /// Generic condition using a delegate.
    /// </summary>
    public class ConditionNode : BTCondition
    {
        private System.Func<bool> condition;
        
        public ConditionNode(string name, System.Func<bool> condition) : base(name)
        {
            this.condition = condition;
        }
        
        public override NodeState Evaluate()
        {
            State = condition() ? NodeState.SUCCESS : NodeState.FAILURE;
            return State;
        }
    }

    /// <summary>
    /// Generic action using a delegate.
    /// </summary>
    public class ActionNode : BTAction
    {
        private System.Func<NodeState> action;
        
        public ActionNode(string name, System.Func<NodeState> action) : base(name)
        {
            this.action = action;
        }
        
        public override NodeState Evaluate()
        {
            State = action();
            return State;
        }
    }

    #endregion

    #region ═══════════════════ BEHAVIOR TREE RUNNER ═══════════════════

    /// <summary>
    /// Main Behavior Tree class that manages execution.
    /// </summary>
    public class BehaviorTreeRunner
    {
        private BTNode root;
        private Dictionary<string, object> blackboard;
        
        /// <summary>Current state of the tree</summary>
        public NodeState State => root?.State ?? NodeState.FAILURE;
        
        /// <summary>Shared data dictionary</summary>
        public Dictionary<string, object> Blackboard => blackboard;
        
        public BehaviorTreeRunner()
        {
            blackboard = new Dictionary<string, object>();
        }
        
        /// <summary>
        /// Set the root node of the tree.
        /// </summary>
        public void SetRoot(BTNode node)
        {
            root = node;
            root.SetBlackboard(blackboard);
        }
        
        /// <summary>
        /// Execute one tick of the behavior tree.
        /// Call this every frame.
        /// </summary>
        public NodeState Tick()
        {
            if (root == null) return NodeState.FAILURE;
            return root.Evaluate();
        }
        
        /// <summary>
        /// Reset the entire tree.
        /// </summary>
        public void Reset()
        {
            root?.Reset();
        }
        
        /// <summary>
        /// Set data in blackboard.
        /// </summary>
        public void SetData(string key, object value)
        {
            blackboard[key] = value;
        }
        
        /// <summary>
        /// Get data from blackboard.
        /// </summary>
        public T GetData<T>(string key)
        {
            if (blackboard.TryGetValue(key, out object value))
            {
                return (T)value;
            }
            return default(T);
        }
    }

    #endregion
}
