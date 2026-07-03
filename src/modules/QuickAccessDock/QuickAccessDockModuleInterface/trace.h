#pragma once

#include <common/Telemetry/TraceBase.h>

class Trace : public telemetry::TraceBase
{
public:
    // Log if the user has Quick Access Dock enabled or disabled.
    static void EnableQuickAccessDock(const bool enabled) noexcept;
};
