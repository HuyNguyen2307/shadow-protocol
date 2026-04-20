using System.Collections.Generic;

/// <summary>
/// Lightweight Behavior Tree framework providing Selector, Sequence, Decorator,
/// Condition, and Action nodes with a shared blackboard for inter-node communication.
///
/// Design philosophy: nodes are stateful and reentrant — each tick resumes from where
/// the previous tick left off rather than restarting from scratch, which allows
/// long-running actions to span multiple frames without extra bookkeeping.
/// </summary>
namespace BehaviorTree
{
    #region Enums

    /// <summary>
    /// Canonical result of a single node evaluation tick.
    /// RUNNING signals the caller that this node needs more ticks to complete.
    /// </summary>
    public enum NodeState
    {
        RUNNING,
        SUCCESS,
        FAILURE
    }

    #endregion


    #region Base Node

    /// <summary>
    /// Abstract base for every node in the tree.
    /// Holds the blackboard reference so all descendant nodes share one data store
    /// without each needing their own pointer managed externally.
    /// </summary>
    public abstract class BTNode
    {
        /// <summary>Result of the most recent <see cref="Evaluate"/> call.</summary>
        public NodeState State { get; protected set; } = NodeState.FAILURE;

        /// <summary>Human-readable label used for debugging and visualization.</summary>
        public string Name { get; set; } = "Node";

        /// <summary>Reference to this node's parent; null on the root node.</summary>
        public BTNode Parent { get; set; }

        /// <summary>
        /// Shared key-value store injected by <see cref="BehaviorTreeRunner"/>.
        /// Kept as <c>Dictionary&lt;string, object&gt;</c> for flexibility; value-type
        /// entries incur boxing overhead — acceptable given BT tick frequencies in games.
        /// </summary>
        protected Dictionary<string, object> _blackboard;

        /// <summary>
        /// Evaluates this node for one tick and returns the resulting <see cref="NodeState"/>.
        /// </summary>
        public abstract NodeState Evaluate();

        /// <summary>
        /// Resets this node to its pre-execution state so the tree can be replayed
        /// or restarted without reconstructing the node graph.
        /// </summary>
        public virtual void Reset() {
            State = NodeState.FAILURE;
        }

        /// <summary>
        /// Propagates the shared blackboard reference down the node hierarchy.
        /// Called once by <see cref="BehaviorTreeRunner.SetRoot"/> so individual nodes
        /// never need to manage their own dictionary instances.
        /// </summary>
        public virtual void SetBlackboard(Dictionary<string, object> bb) {
            _blackboard = bb;
        }

        /// <summary>
        /// Reads a typed value from the blackboard, returning <c>default(T)</c>
        /// when the key is absent rather than throwing, so callers can treat a
        /// missing key as "not yet set" without extra existence checks.
        /// </summary>
        protected T GetData<T>(string key) {
            if (_blackboard != null && _blackboard.TryGetValue(key, out object value))
                return (T)value;

            return default(T);
        }

        /// <summary>
        /// Writes a value into the blackboard.
        /// The null guard exists because <see cref="SetBlackboard"/> may not have been
        /// called if a node is evaluated in isolation during unit testing.
        /// </summary>
        protected void SetData(string key, object value) {
            if (_blackboard != null)
                _blackboard[key] = value;
        }

        /// <summary>
        /// Returns true when the blackboard contains <paramref name="key"/>.
        /// Nodes use this to distinguish "key absent" from "key present but null".
        /// </summary>
        protected bool HasData(string key) {
            return _blackboard != null && _blackboard.ContainsKey(key);
        }
    }

    #endregion


    #region Composite Nodes

    /// <summary>
    /// Base class for nodes that own an ordered list of child nodes.
    /// Maintains <see cref="_activeChildIndex"/> so a composite can resume mid-list
    /// across ticks when a child returns RUNNING.
    /// </summary>
    public abstract class BTComposite : BTNode
    {
        protected List<BTNode> _children = new List<BTNode>();
        protected int _activeChildIndex = 0;

        public BTComposite(string name = "Composite") {
            Name = name;
        }

        /// <summary>
        /// Attaches a child to this composite and wires the parent back-reference,
        /// which the debugger uses to walk the tree upward from any node.
        /// Returns <c>this</c> to support fluent builder chains.
        /// </summary>
        public BTComposite AddChild(BTNode child) {
            child.Parent = this;
            _children.Add(child);
            return this;
        }

        /// <summary>
        /// Convenience overload for adding several children in one call.
        /// Returns <c>this</c> to support fluent builder chains.
        /// </summary>
        public BTComposite AddChildren(params BTNode[] nodes) {
            foreach (var node in nodes)
                AddChild(node);

            return this;
        }

        /// <summary>Read-only view of this composite's child list.</summary>
        public List<BTNode> Children => _children;

        public override void Reset() {
            base.Reset();
            _activeChildIndex = 0;
            foreach (var child in _children)
                child.Reset();
        }

        public override void SetBlackboard(Dictionary<string, object> bb) {
            base.SetBlackboard(bb);
            foreach (var child in _children)
                child.SetBlackboard(bb);
        }
    }

    /// <summary>
    /// OR-node: succeeds as soon as any child succeeds.
    /// Models fallback priority — earlier children are preferred over later ones,
    /// which lets designers express "try the best option first, fall back if it fails."
    ///
    /// A child returning RUNNING suspends iteration so that child continues next tick,
    /// preventing redundant re-evaluation of already-passed siblings.
    /// </summary>
    public class Selector : BTComposite
    {
        public Selector(string name = "Selector") : base(name) { }

        public override NodeState Evaluate() {
            for (int i = _activeChildIndex; i < _children.Count; i++)
            {
                _activeChildIndex = i;

                switch (_children[i].Evaluate())
                {
                    case NodeState.SUCCESS:
                        _activeChildIndex = 0;
                        return State = NodeState.SUCCESS;

                    case NodeState.RUNNING:
                        return State = NodeState.RUNNING;

                    case NodeState.FAILURE:
                        continue;
                }
            }

            _activeChildIndex = 0;
            return State = NodeState.FAILURE;
        }
    }

    /// <summary>
    /// AND-node: succeeds only when every child succeeds in order.
    /// Models precondition chains — a single failure aborts the remaining steps,
    /// which avoids executing expensive actions when early conditions aren't met.
    ///
    /// A child returning RUNNING suspends iteration so the in-progress child
    /// resumes on the next tick rather than restarting from the first child.
    /// </summary>
    public class Sequence : BTComposite
    {
        public Sequence(string name = "Sequence") : base(name) { }

        public override NodeState Evaluate() {
            for (int i = _activeChildIndex; i < _children.Count; i++)
            {
                _activeChildIndex = i;

                switch (_children[i].Evaluate())
                {
                    case NodeState.SUCCESS:
                        continue;

                    case NodeState.RUNNING:
                        return State = NodeState.RUNNING;

                    case NodeState.FAILURE:
                        _activeChildIndex = 0;
                        return State = NodeState.FAILURE;
                }
            }

            _activeChildIndex = 0;
            return State = NodeState.SUCCESS;
        }
    }

    #endregion


    #region Decorator Nodes

    /// <summary>
    /// Abstract base for nodes that wrap exactly one child and modify its result or
    /// control how many times it runs. Propagates blackboard and reset signals downward.
    /// </summary>
    public abstract class BTDecorator : BTNode
    {
        protected BTNode _child;

        public BTDecorator(BTNode child, string name = "Decorator") {
            _child = child;
            if (_child != null)
                _child.Parent = this;

            Name = name;
        }

        public override void Reset() {
            base.Reset();
            _child?.Reset();
        }

        public override void SetBlackboard(Dictionary<string, object> bb) {
            base.SetBlackboard(bb);
            _child?.SetBlackboard(bb);
        }
    }

    /// <summary>
    /// Flips SUCCESS to FAILURE and vice versa; RUNNING passes through unchanged.
    /// Useful for expressing "this condition must NOT hold" without a dedicated
    /// negative condition class for every check in the game.
    ///
    /// The null guard is retained because child can be legally omitted during
    /// prototyping — failing safe is preferable to a NullReferenceException mid-tick.
    /// </summary>
    public class Inverter : BTDecorator
    {
        public Inverter(BTNode child) : base(child, "Inverter") { }

        public override NodeState Evaluate() {
            if (_child == null)
                return State = NodeState.FAILURE;

            switch (_child.Evaluate())
            {
                case NodeState.SUCCESS: return State = NodeState.FAILURE;
                case NodeState.FAILURE: return State = NodeState.SUCCESS;
                default:               return State = NodeState.RUNNING;
            }
        }
    }

    /// <summary>
    /// Always returns SUCCESS regardless of the child's result, including when
    /// the child is null. Used to mark optional sub-trees that should never block
    /// a parent Sequence from proceeding.
    /// </summary>
    public class Succeeder : BTDecorator
    {
        public Succeeder(BTNode child) : base(child, "Succeeder") { }

        public override NodeState Evaluate() {
            _child?.Evaluate();
            return State = NodeState.SUCCESS;
        }
    }

    /// <summary>
    /// Keeps returning RUNNING until the child returns FAILURE, at which point
    /// the decorator returns SUCCESS. This lets designers express "keep doing X
    /// until it can no longer run" as a single composable unit rather than
    /// embedding loop logic inside action nodes.
    ///
    /// The null guard is retained for the same reason as <see cref="Inverter"/>.
    /// </summary>
    public class RepeatUntilFail : BTDecorator
    {
        public RepeatUntilFail(BTNode child) : base(child, "RepeatUntilFail") { }

        public override NodeState Evaluate() {
            if (_child == null)
                return State = NodeState.FAILURE;

            if (_child.Evaluate() == NodeState.FAILURE)
                return State = NodeState.SUCCESS;

            return State = NodeState.RUNNING;
        }
    }

    #endregion


    #region Leaf Nodes

    /// <summary>
    /// Semantic base for nodes that test world state.
    /// Conditions must never return RUNNING — they exist to gate action sub-trees
    /// based on a single synchronous predicate.
    /// </summary>
    public abstract class BTCondition : BTNode
    {
        public BTCondition(string name = "Condition") {
            Name = name;
        }
    }

    /// <summary>
    /// Semantic base for nodes that change world state or trigger game systems.
    /// Actions may return RUNNING to signal work is still in progress across ticks.
    /// </summary>
    public abstract class BTAction : BTNode
    {
        public BTAction(string name = "Action") {
            Name = name;
        }
    }

    /// <summary>
    /// Inline condition backed by a delegate, eliminating boilerplate subclasses for
    /// simple boolean checks that don't require their own state or blackboard access.
    /// </summary>
    public class ConditionNode : BTCondition
    {
        private readonly System.Func<bool> _condition;

        public ConditionNode(string name, System.Func<bool> condition) : base(name) {
            _condition = condition;
        }

        public override NodeState Evaluate() {
            return State = _condition() ? NodeState.SUCCESS : NodeState.FAILURE;
        }
    }

    /// <summary>
    /// Inline action backed by a delegate, eliminating boilerplate subclasses for
    /// simple actions that don't require their own state or blackboard access.
    /// The delegate is responsible for returning the correct <see cref="NodeState"/>,
    /// including RUNNING for multi-tick operations.
    /// </summary>
    public class ActionNode : BTAction
    {
        private readonly System.Func<NodeState> _action;

        public ActionNode(string name, System.Func<NodeState> action) : base(name) {
            _action = action;
        }

        public override NodeState Evaluate() {
            return State = _action();
        }
    }

    #endregion


    #region Behavior Tree Runner

    /// <summary>
    /// Entry point for executing a behavior tree.
    /// Owns the root node and the shared blackboard, acting as the single surface
    /// through which MonoBehaviours drive the AI each frame via <see cref="Tick"/>.
    /// </summary>
    public class BehaviorTreeRunner
    {
        private BTNode _root;
        private readonly Dictionary<string, object> _blackboard;

        /// <summary>
        /// The most recent state returned by the root node.
        /// Returns FAILURE when no root has been set, matching the fail-safe convention
        /// used throughout the framework.
        /// </summary>
        public NodeState State => _root?.State ?? NodeState.FAILURE;

        /// <summary>
        /// Direct access to the shared blackboard.
        /// Exposed so the owning MonoBehaviour can seed or read data without going
        /// through the typed helper methods.
        ///
        /// Note: stores all values as <c>object</c>, so value types are boxed.
        /// This is a known tradeoff — acceptable at BT tick rates in this project.
        /// </summary>
        public Dictionary<string, object> Blackboard => _blackboard;

        public BehaviorTreeRunner() {
            _blackboard = new Dictionary<string, object>();
        }

        /// <summary>
        /// Assigns the root node and propagates the blackboard reference down
        /// the entire node graph so subsequent <see cref="Tick"/> calls work correctly.
        /// Must be called before the first <see cref="Tick"/>.
        /// </summary>
        public void SetRoot(BTNode node) {
            _root = node;
            _root.SetBlackboard(_blackboard);
        }

        /// <summary>
        /// Advances the tree by one evaluation step.
        /// Should be called once per frame from the owning MonoBehaviour's Update.
        /// Returns FAILURE immediately when no root has been set to prevent null faults
        /// during late initialization.
        /// </summary>
        public NodeState Tick() {
            if (_root == null)
                return NodeState.FAILURE;

            return _root.Evaluate();
        }

        /// <summary>
        /// Resets the entire tree back to its initial state.
        /// Call this when the AI re-enters an active state after being disabled,
        /// rather than reconstructing the node graph from scratch each time.
        /// </summary>
        public void Reset() {
            _root?.Reset();
        }

        /// <summary>
        /// Writes <paramref name="value"/> into the blackboard under <paramref name="key"/>.
        /// </summary>
        public void SetData(string key, object value) {
            _blackboard[key] = value;
        }

        /// <summary>
        /// Reads a typed value from the blackboard.
        /// Returns <c>default(T)</c> when the key is absent — treat a missing key
        /// as "not yet written" rather than an error condition.
        /// </summary>
        public T GetData<T>(string key) {
            if (_blackboard.TryGetValue(key, out object value))
                return (T)value;

            return default(T);
        }
    }

    #endregion
}
