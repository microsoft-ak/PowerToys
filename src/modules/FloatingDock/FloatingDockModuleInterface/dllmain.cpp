#include "pch.h"

#include "FloatingDockConstants.h"
#include "resource.h"

#include <interface/powertoy_module_interface.h>

#include <common/SettingsAPI/settings_objects.h>
#include <common/logger/logger.h>
#include <common/SettingsAPI/settings_helpers.h>
#include <common/utils/logger_helper.h>
#include <common/utils/resources.h>
#include <common/utils/winapi_error.h>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace
{
    const wchar_t* MODULE_DESCRIPTION = L"A floating always-on-top dock for quick access to files, folders, apps, URLs, shell locations, and commands.";
    const wchar_t* DOCK_PROCESS_NAME = L"PowerToys.FloatingDock.exe";
}

BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID)
{
    return TRUE;
}

class FloatingDockModule : public PowertoyModuleIface
{
public:
    FloatingDockModule()
    {
        app_name = GET_RESOURCE_STRING(IDS_FLOATING_DOCK_NAME);
        app_key = FloatingDockConstants::ModuleKey;
        LoggerHelpers::init_logger(app_key, L"ModuleInterface", "FloatingDock");
    }

    virtual void destroy() override
    {
        disable();
        delete this;
    }

    virtual const wchar_t* get_name() override
    {
        return app_name.c_str();
    }

    virtual const wchar_t* get_key() override
    {
        return app_key.c_str();
    }

    virtual bool get_config(wchar_t* buffer, int* buffer_size) override
    {
        HINSTANCE hinstance = reinterpret_cast<HINSTANCE>(&__ImageBase);
        PowerToysSettings::Settings settings(hinstance, get_name());
        settings.set_description(MODULE_DESCRIPTION);
        settings.set_overview_link(L"https://aka.ms/PowerToysOverview_FloatingDock");

        return settings.serialize_to_buffer(buffer, buffer_size);
    }

    virtual void set_config(const wchar_t* config) override
    {
        try
        {
            PowerToysSettings::PowerToyValues values =
                PowerToysSettings::PowerToyValues::from_json_string(config, get_key());
            values.save_to_settings_file();
        }
        catch (...)
        {
            Logger::warn(L"Floating Dock received invalid settings JSON.");
        }
    }

    virtual void enable() override
    {
        if (is_process_running())
        {
            m_enabled = true;
            return;
        }

        launch_process();
    }

    virtual void disable() override
    {
        if (m_enabled)
        {
            stop_process();
        }

        m_enabled = false;
    }

    virtual bool is_enabled() override
    {
        return m_enabled;
    }

    virtual bool is_enabled_by_default() const override
    {
        return false;
    }

private:
    std::wstring app_name;
    std::wstring app_key;
    bool m_enabled = false;
    PROCESS_INFORMATION process_info = {};
    HANDLE exit_event = nullptr;
    std::wstring exit_event_name;

    bool is_process_running() const
    {
        return process_info.hProcess != nullptr && WaitForSingleObject(process_info.hProcess, 0) == WAIT_TIMEOUT;
    }

    void launch_process()
    {
        close_process_handles();

        const auto current_pid = GetCurrentProcessId();
        exit_event_name = std::format(L"Local\\PowerToys_FloatingDock_Exit_{}", current_pid);
        exit_event = CreateEventW(nullptr, TRUE, FALSE, exit_event_name.c_str());
        if (exit_event == nullptr)
        {
            Logger::error(L"Floating Dock failed to create exit event. {}", get_last_error_or_default(GetLastError()));
            return;
        }

        std::wstring command_line = std::format(L"\"{}\" --pid {} --exit-event \"{}\"", DOCK_PROCESS_NAME, current_pid, exit_event_name);
        STARTUPINFO startup_info = { sizeof(startup_info) };

        if (!CreateProcessW(DOCK_PROCESS_NAME, command_line.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup_info, &process_info))
        {
            Logger::error(L"Floating Dock failed to start. {}", get_last_error_or_default(GetLastError()));
            close_process_handles();
            return;
        }

        Logger::trace(L"Floating Dock process started.");
        m_enabled = true;
    }

    void stop_process()
    {
        if (exit_event != nullptr)
        {
            SetEvent(exit_event);
        }

        if (process_info.hProcess != nullptr)
        {
            WaitForSingleObject(process_info.hProcess, 5000);
            if (is_process_running())
            {
                Logger::warn(L"Floating Dock did not exit after signal; terminating process.");
                TerminateProcess(process_info.hProcess, 1);
            }
        }

        close_process_handles();
    }

    void close_process_handles()
    {
        if (process_info.hThread != nullptr)
        {
            CloseHandle(process_info.hThread);
            process_info.hThread = nullptr;
        }

        if (process_info.hProcess != nullptr)
        {
            CloseHandle(process_info.hProcess);
            process_info.hProcess = nullptr;
        }

        if (exit_event != nullptr)
        {
            CloseHandle(exit_event);
            exit_event = nullptr;
        }
    }
};

extern "C" __declspec(dllexport) PowertoyModuleIface* __cdecl powertoy_create()
{
    return new FloatingDockModule();
}
