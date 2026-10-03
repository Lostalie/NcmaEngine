#pragma once

#include "scene/SceneWorld.h"

#include <optional>
#include <string>
#include <vector>

namespace NcmaEngine
{
    class SceneCommandStack final
    {
    public:
        struct State final
        {
            SceneSnapshot Scene;
            std::optional<SceneUuid> Selection;
        };

        void Push(std::string label, State before, State after);
        [[nodiscard]] bool Undo(SceneWorld& world, std::optional<SceneUuid>& selection, std::string& error);
        [[nodiscard]] bool Redo(SceneWorld& world, std::optional<SceneUuid>& selection, std::string& error);
        void Clear() noexcept;
        void MarkSaved() noexcept;

        [[nodiscard]] bool CanUndo() const noexcept { return m_Cursor > 0; }
        [[nodiscard]] bool CanRedo() const noexcept { return m_Cursor < m_Commands.size(); }
        [[nodiscard]] bool IsDirty() const noexcept;
        [[nodiscard]] const std::string& UndoLabel() const;
        [[nodiscard]] const std::string& RedoLabel() const;

    private:
        struct Command final
        {
            std::string Label;
            State Before;
            State After;
        };

        std::vector<Command> m_Commands;
        std::size_t m_Cursor = 0;
        std::optional<std::size_t> m_SavedCursor = 0;
    };
}
