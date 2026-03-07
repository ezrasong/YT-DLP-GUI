fn main() {
    // Expose the target triple so lib.rs can resolve sidecar binary names
    println!(
        "cargo:rustc-env=TARGET_TRIPLE={}",
        std::env::var("TARGET").unwrap()
    );
    tauri_build::build()
}
