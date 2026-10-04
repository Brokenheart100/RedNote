from __future__ import annotations

from datetime import datetime
from pathlib import Path
import shutil
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parent.parent

APPHOST_PROJECT = (
    ROOT
    / "RedNote.AppHost"
    / "RedNote.AppHost.csproj"
)

OUTPUT_PATH = (
    ROOT
    / "RedNote.AppHost"
    / "aspire-output"
)

ASPIRE_EXECUTABLE = shutil.which("aspire")
DOTNET_EXECUTABLE = shutil.which("dotnet")
DOCKER_EXECUTABLE = shutil.which("docker")


def print_header(
    title: str,
) -> None:
    print()
    print("=" * 80)
    print(f"🚀 {title}")
    print("=" * 80)


def print_info(
    label: str,
    value: object,
) -> None:
    print(f"ℹ️  {label}: {value}")


def format_command(
    args: list[str],
) -> str:
    return " ".join(
        f'"{arg}"'
        if " " in arg
        else arg
        for arg in args
    )


def run_capture(
    args: list[str],
) -> tuple[int, str]:
    try:
        result = subprocess.run(
            args,
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
            encoding="utf-8",
            errors="replace",
        )
    except OSError as exc:
        return 1, str(exc)

    stdout = result.stdout or ""
    stderr = result.stderr or ""

    output = (
        stdout
        + stderr
    ).strip()

    return result.returncode, output


def get_version(
    command: list[str],
) -> str:
    return_code, output = run_capture(
        command
    )

    if return_code != 0:
        return "不可用"

    if not output:
        return "未知"

    return output.splitlines()[0].strip()


def check_docker_engine() -> bool:
    if DOCKER_EXECUTABLE is None:
        print(
            "❌ 未找到 Docker CLI。"
        )
        return False

    return_code, output = run_capture(
        [
            DOCKER_EXECUTABLE,
            "info",
            "--format",
            "{{.ServerVersion}}",
        ]
    )

    if return_code != 0:
        print()
        print(
            "❌ Docker Engine 当前不可用。"
        )

        if output:
            print()
            print(output)

        print()
        print(
            "💡 请确认 Docker Desktop "
            "已经启动并且 Engine 正常运行。"
        )

        return False

    print(
        f"✅ Docker Engine: {output}"
    )

    return True


def validate_environment() -> bool:
    print_header(
        "环境检查"
    )

    print_info(
        "Solution Root",
        ROOT,
    )

    print_info(
        "AppHost",
        APPHOST_PROJECT,
    )

    print_info(
        "Deploy Output",
        OUTPUT_PATH,
    )

    print()

    if not APPHOST_PROJECT.exists():
        print(
            f"❌ AppHost 项目不存在：{APPHOST_PROJECT}"
        )
        return False

    if ASPIRE_EXECUTABLE is None:
        print(
            "❌ 未找到 Aspire CLI。"
        )
        print(
            "💡 请先在当前终端执行："
        )
        print(
            "   where.exe aspire"
        )
        return False

    if DOTNET_EXECUTABLE is None:
        print(
            "❌ 未找到 .NET SDK。"
        )
        return False

    if DOCKER_EXECUTABLE is None:
        print(
            "❌ 未找到 Docker CLI。"
        )
        return False

    print(
        f"✅ Aspire CLI: {ASPIRE_EXECUTABLE}"
    )

    print(
        "✅ Aspire Version: "
        f"{get_version([ASPIRE_EXECUTABLE, '--version'])}"
    )

    print(
        f"✅ .NET SDK: {DOTNET_EXECUTABLE}"
    )

    print(
        "✅ .NET Version: "
        f"{get_version([DOTNET_EXECUTABLE, '--version'])}"
    )

    print(
        f"✅ Docker CLI: {DOCKER_EXECUTABLE}"
    )

    print(
        "✅ Docker Version: "
        f"{get_version([DOCKER_EXECUTABLE, '--version'])}"
    )

    if not check_docker_engine():
        return False

    return True


def print_command(
    args: list[str],
) -> None:
    print()
    print(
        "💻 执行命令："
    )
    print()
    print(
        f"   {format_command(args)}"
    )


def print_docker_status() -> None:
    print_header(
        "Docker 容器状态"
    )

    if DOCKER_EXECUTABLE is None:
        print(
            "⚠️ Docker CLI 不可用。"
        )
        return

    command = [
        DOCKER_EXECUTABLE,
        "ps",
        "-a",
        "--format",
        (
            "table "
            "{{.Names}}\t"
            "{{.Status}}\t"
            "{{.Image}}\t"
            "{{.Ports}}"
        ),
    ]

    print_command(
        command
    )

    print()

    try:
        subprocess.run(
            command,
            cwd=ROOT,
            check=False,
        )
    except OSError as exc:
        print(
            "⚠️ 无法读取 Docker "
            f"容器状态：{exc}"
        )


def print_failed_containers() -> None:
    if DOCKER_EXECUTABLE is None:
        return

    return_code, output = run_capture(
        [
            DOCKER_EXECUTABLE,
            "ps",
            "-a",
            "--filter",
            "status=exited",
            "--format",
            "{{.Names}}\t{{.Status}}",
        ]
    )

    if return_code != 0:
        return

    if not output:
        return

    print()
    print(
        "⚠️ 当前存在已退出容器："
    )

    print()

    for line in output.splitlines():
        print(
            f"   ❌ {line}"
        )


def deploy() -> int:
    if ASPIRE_EXECUTABLE is None:
        print(
            "❌ Aspire CLI 路径不可用。"
        )
        return 1

    print_header(
        "RedNote Aspire Deploy"
    )

    started_at = datetime.now()
    started = time.perf_counter()

    print_info(
        "开始时间",
        started_at.strftime(
            "%Y-%m-%d %H:%M:%S"
        ),
    )

    command = [
        ASPIRE_EXECUTABLE,
        "deploy",
        "--apphost",
        str(APPHOST_PROJECT),
        "--output-path",
        str(OUTPUT_PATH),
        "--pipeline-log-level",
        "trace",
        "--include-exception-details",
        "--log-level",
        "Debug",
    ]

    print_command(
        command
    )

    print()
    print(
        "🐳 开始执行 Aspire Deploy..."
    )

    print(
        "📋 以下为 Aspire 实时输出："
    )

    print()
    print("-" * 80)

    try:
        result = subprocess.run(
            command,
            cwd=ROOT,
            check=False,
        )
    except KeyboardInterrupt:
        print()
        print("-" * 80)
        print()
        print(
            "🛑 Deploy 已被用户中断。"
        )
        return 130
    except OSError as exc:
        print()
        print("-" * 80)
        print()
        print(
            f"❌ 无法启动 Aspire CLI：{exc}"
        )
        print(
            f"📍 Aspire 路径：{ASPIRE_EXECUTABLE}"
        )
        return 1

    elapsed = (
        time.perf_counter()
        - started
    )

    finished_at = datetime.now()

    print()
    print("-" * 80)

    print_header(
        "Deploy 执行结果"
    )

    print_info(
        "结束时间",
        finished_at.strftime(
            "%Y-%m-%d %H:%M:%S"
        ),
    )

    print_info(
        "总耗时",
        f"{elapsed:.2f} 秒",
    )

    print_info(
        "退出代码",
        result.returncode,
    )

    print_docker_status()
    print_failed_containers()

    if result.returncode != 0:
        print()
        print(
            "❌ Aspire Deploy 失败。"
        )

        print()
        print(
            "🔎 请优先检查上面的 "
            "Aspire Pipeline 失败步骤。"
        )

        print(
            "🐳 然后检查状态为 "
            "Exited / Restarting 的容器。"
        )

        print()
        print(
            "💡 查看指定容器日志："
        )

        print(
            "   docker logs <容器名>"
        )

        print()
        print(
            "💡 查看最近 200 行："
        )

        print(
            "   docker logs --tail 200 <容器名>"
        )

        return result.returncode

    print()
    print(
        "✅ Aspire Deploy 成功。"
    )

    print()
    print(
        "🌐 本机测试入口："
    )

    print(
        "   🖥️ Frontend: "
        "http://localhost:3000"
    )

    print(
        "   🚪 Gateway:  "
        "http://localhost:8080"
    )

    print()
    print(
        "🔐 OpenID Connect Discovery:"
    )

    print(
        "   http://localhost:8080/"
        ".well-known/openid-configuration"
    )

    return 0


def main() -> None:
    if not validate_environment():
        sys.exit(1)

    exit_code = deploy()

    sys.exit(
        exit_code
    )


if __name__ == "__main__":
    main()