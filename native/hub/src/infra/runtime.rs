use once_cell::sync::OnceCell;
use tokio::runtime::Runtime;

static RUNTIME: OnceCell<Runtime> = OnceCell::new();

pub fn install(rt: Runtime) -> Result<(), &'static str> {
    RUNTIME.set(rt).map_err(|_| "runtime already installed")
}

pub fn handle() -> Option<tokio::runtime::Handle> {
    RUNTIME.get().map(|rt| rt.handle().clone())
}
