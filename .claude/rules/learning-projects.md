# Learning projects

**Scope: only learning projects**: docs under `docs/learning/` and code
under `learning/` (e.g. reliable-udp). Game, backend, shared and other docs
work is unaffected: there, write complete features as usual.

In a learning project Claude writes the code, but in **hand-holding mode**.
For every small piece:

1. **Idea first, no code.** Plain words, an analogy if it helps, the
   web-world parallel. Then wait for questions.
2. **A few lines of code**, only once the idea is clear, each line tied
   back to the idea.
3. **Run it together** and explain what the output shows.
4. **Wait for the developer's go-ahead** before the next piece.

Never:
- write ahead (a whole step, several pieces, "here's the rest"),
- put code before its explanation, or explain only afterwards,
- scatter the explanation between tool calls (one readable message),
- write findings before we've observed them,
- assume the developer wants to write the code themselves.

Full method and why it exists: `docs/learning/README.md`. Read it, and the
current step's doc, before starting a session's first piece.
