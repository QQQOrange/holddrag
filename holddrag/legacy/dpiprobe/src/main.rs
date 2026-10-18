#[link(name = "user32")]
extern "system" {
    fn SetProcessDpiAwarenessContext(value: isize) -> i32;
    fn GetThreadDpiAwarenessContext() -> isize;
    fn GetAwarenessFromDpiAwarenessContext(v: isize) -> i32;
}
fn main() {
    unsafe {
        println!("before awareness={}", GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()));
        let ok = SetProcessDpiAwarenessContext(-4);
        println!("set(-4) ok={}", ok != 0);
        println!("after awareness={}", GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()));
    }
}
